using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Help;
using Substratal.Hub.Core.ViewModels;
using Substratal.Hub.Services;
using Windows.System;

namespace Substratal.Hub.Views;

/// <summary>AUTH-01 Sign in and AUTH-02 Two-factor challenge: full window, before the shell.</summary>
public sealed partial class SignInPage : Page
{
    public SignInViewModel ViewModel { get; } = App.Services.GetRequiredService<SignInViewModel>();

    public SignInPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.SignedIn += OnSignedIn;
        ActualThemeChanged += (_, _) => UpdateLockup();
        UpdateLockup();
        UpdateState();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        EndedBar.IsOpen = e.Parameter is SessionEndReason.Revoked;
        EndedBar.Message = "Your session ended. Sign in again.";
        EmailBox.Focus(FocusState.Programmatic);
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignInViewModel.Step) or nameof(SignInViewModel.ErrorMessage) or nameof(SignInViewModel.SelectedEnvironment))
            UpdateState();
        if (e.PropertyName == nameof(SignInViewModel.Password) && ViewModel.Password.Length == 0) PasswordBox.Password = "";
    }

    void UpdateState()
    {
        var twoFactor = ViewModel.Step == SignInStep.TwoFactor;
        CredentialsPanel.Visibility = twoFactor ? Visibility.Collapsed : Visibility.Visible;
        TwoFactorPanel.Visibility = twoFactor ? Visibility.Visible : Visibility.Collapsed;
        if (twoFactor) CodeBox.Focus(FocusState.Programmatic);

        ErrorBar.IsOpen = ViewModel.ErrorMessage is not null;
        ErrorBar.Message = ViewModel.ErrorMessage ?? "";
        ErrorBar.Title = ViewModel.RequestId is { } id ? $"Request ID {id}" : "";

        // Test-mode sign-in is for developer builds and non-production environments (OPEN-QUESTIONS #13).
        TestModeBox.Visibility = ViewModel.SelectedEnvironment.IsProduction ? Visibility.Collapsed : Visibility.Visible;
    }

    void UpdateLockup()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        Lockup.Source = new SvgImageSource(new Uri($"ms-appx:///Assets/lockup-inline{(dark ? "-on-dark" : "")}.svg"));
    }

    void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = PasswordBox.Password;

    void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SignInCommand.CanExecute(null)) ViewModel.SignInCommand.Execute(null);
    }

    void OnCodeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.VerifyCommand.CanExecute(null)) ViewModel.VerifyCommand.Execute(null);
    }

    void OnRecoveryToggled(object sender, RoutedEventArgs e) =>
        TwoFactorPrompt.Text = ViewModel.UseRecoveryCode
            ? "Enter one of the recovery codes you saved when you turned on two-factor authentication."
            : "Enter the 6-digit code from your authenticator app.";

    void OnSignedIn(object? sender, string userId)
    {
        App.Services.GetRequiredService<AppPreferences>().Environment = ViewModel.SelectedEnvironment;
        App.MainWindow.ShowShell();
    }

    void OnForgotPassword(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ScreenPage), "AUTH-05");
    void OnCreateAccount(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ScreenPage), "AUTH-03");

    async void OnHelp(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(HelpLinks.ForScreen(ViewModel.Step == SignInStep.TwoFactor ? "AUTH-02" : "AUTH-01"));
}
