using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Substratal.Hub.Core.Help;
using Substratal.Hub.Core.Navigation;
using Windows.System;

namespace Substratal.Hub.Views;

/// <summary>Any screen without its own page yet. Opened by the shell (a <see cref="ScreenTarget"/>) or from sign-in (an id).</summary>
public sealed partial class ScreenPage : Page, IScreenPage
{
    public ScreenTarget? Target { get; private set; }

    public ScreenPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Target = e.Parameter switch
        {
            ScreenTarget t => t,
            string id => new ScreenTarget(id, null),
            _ => null,
        };
        if (Target is null || ScreenCatalog.Find(Target.ScreenId) is not { } screen) return;

        // Opened outside the shell (AUTH-03, AUTH-05 from sign-in): no page header, so show the title and a way back.
        var outside = e.Parameter is string;
        BackLink.Visibility = outside ? Visibility.Visible : Visibility.Collapsed;
        OutsideTitle.Visibility = outside ? Visibility.Visible : Visibility.Collapsed;
        OutsideTitle.Text = screen.Title;

        IdText.Text = screen.Id;
        PurposeText.Text = screen.Purpose;
        WhoText.Text = screen.Who;
        DomainText.Text = $"{screen.Domain} · {screen.DomainTitle}";
        RouteText.Text = screen.Route;
        PhaseText.Text = screen.Phase;
        ContainerText.Text = screen.WindowsContainer.Replace("`", "");
        ParameterText.Visibility = Target.Parameter is null ? Visibility.Collapsed : Visibility.Visible;
        ParameterText.Text = Target.Parameter ?? "";
    }

    void OnBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    async void OnHelp(object sender, RoutedEventArgs e)
    {
        if (Target is not null) await Launcher.LaunchUriAsync(HelpLinks.ForScreen(Target.ScreenId));
    }

    /// <summary>The Windows screen specs on GitHub, where this screen's controls and data are defined.</summary>
    async void OnSpec(object sender, RoutedEventArgs e)
    {
        if (Target is null) return;
        await Launcher.LaunchUriAsync(new Uri(
            "https://github.com/CompositeCode/substratalapps-applications-specs/blob/main/substratal-application-specs/windows/SCREENS.md"));
    }
}
