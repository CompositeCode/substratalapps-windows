using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Substratal.Hub.Core.Api;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Help;
using Substratal.Hub.Core.ViewModels;
using Substratal.Hub.Services;
using Substratal.Hub.Theming;

namespace Substratal.Hub.Views;

/// <summary>SET-01 Settings: Appearance (THEMES.md), help links and About.</summary>
public sealed partial class SettingsPage : Page, IScreenPage
{
    static readonly (ThemeMode Mode, string Label)[] Modes = [(ThemeMode.System, "Match Windows"), (ThemeMode.Light, "Light"), (ThemeMode.Dark, "Dark")];

    readonly AppPreferences preferences = App.Services.GetRequiredService<AppPreferences>();
    readonly ThemeService themes = App.Services.GetRequiredService<ThemeService>();
    bool loading;

    public ScreenTarget? Target { get; private set; }

    public SettingsPage()
    {
        InitializeComponent();
        WindowsGuideLink.NavigateUri = HelpLinks.WindowsApp;
        HelpCenterLink.NavigateUri = HelpLinks.Root;
        FaqLink.NavigateUri = HelpLinks.Faq;
        ApiReferenceLink.NavigateUri = HelpLinks.ApiReference;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Target = e.Parameter as ScreenTarget;
        loading = true;
        ModeBox.ItemsSource = Modes.Select(m => m.Label).ToList();
        ModeBox.SelectedIndex = Array.FindIndex(Modes, m => m.Mode == preferences.ThemeMode);
        LightBox.ItemsSource = Enum.GetValues<LightTheme>().Select(Label).ToList();
        LightBox.SelectedIndex = Array.IndexOf(Enum.GetValues<LightTheme>(), preferences.LightTheme);
        DarkBox.ItemsSource = Enum.GetValues<DarkTheme>().Select(Label).ToList();
        DarkBox.SelectedIndex = Array.IndexOf(Enum.GetValues<DarkTheme>(), preferences.DarkTheme);
        loading = false;

        var session = App.Services.GetRequiredService<SessionManager>();
        var shell = App.Services.GetRequiredService<ShellViewModel>();
        VersionText.Text = $"Substratal Hub for Windows {AppInfo.Version}";
        ApiText.Text = $"{session.Environment.BaseUrl}  ·  {ApiVersion.HeaderName}: {ApiVersion.Current}";
        AccountText.Text = $"{shell.Capabilities.Email}  ·  {session.UserId}";
    }

    void OnAppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ModeBox.SelectedIndex < 0 || LightBox.SelectedIndex < 0 || DarkBox.SelectedIndex < 0) return;
        preferences.ThemeMode = Modes[ModeBox.SelectedIndex].Mode;
        preferences.LightTheme = Enum.GetValues<LightTheme>()[LightBox.SelectedIndex];
        preferences.DarkTheme = Enum.GetValues<DarkTheme>()[DarkBox.SelectedIndex];
        if (App.MainWindow.Content is FrameworkElement root)
            themes.Apply(root, preferences.ThemeMode, preferences.LightTheme, preferences.DarkTheme);
    }

    async void OnSignOut(object sender, RoutedEventArgs e) =>
        await App.Services.GetRequiredService<SessionManager>().SignOutAsync();

    /// <summary>"MossPaper" → "Moss Paper".</summary>
    static string Label<T>(T value) where T : struct, Enum =>
        string.Join(' ', ThemeService.Id(value).Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}
