using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Substratal.Hub.Core.Api;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Capabilities;
using Substratal.Hub.Core.Navigation;

namespace Substratal.Hub.Core.ViewModels;

/// <summary>A GLB-03 app-wide banner.</summary>
public sealed record Banner(string Message, string? ActionLabel, bool Blocking);

/// <summary>
/// The signed-in shell: which NavigationView items show (INFORMATION-ARCHITECTURE §2), who's signed in, and the
/// GLB-03 version banners.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    readonly SessionManager session;
    readonly CapabilitiesService capabilities;
    readonly FeatureFlags flags;
    readonly ApiVersionStatus version;

    public ShellViewModel(SessionManager session, CapabilitiesService capabilities, FeatureFlags flags, ApiVersionStatus version)
    {
        (this.session, this.capabilities, this.flags, this.version) = (session, capabilities, flags, version);
        capabilities.Changed += (_, _) => Refresh();
        version.Changed += (_, _) => VersionBanner = BannerFor(version);
        Refresh();
    }

    [ObservableProperty] public partial IReadOnlyList<NavItem> Personal { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<NavItem> Administration { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<NavItem> Developer { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<NavItem> Footer { get; set; } = [];
    [ObservableProperty] public partial string AccountEmail { get; set; } = "";
    [ObservableProperty] public partial string EnvironmentName { get; set; } = "";
    [ObservableProperty] public partial bool IsProduction { get; set; } = true;
    [ObservableProperty] public partial bool IsTestMode { get; set; }
    [ObservableProperty] public partial Banner? VersionBanner { get; set; }

    /// <summary>Staff without two-factor are sent to ACC-05 before anything else (PLAN.md §3.1).</summary>
    [ObservableProperty] public partial bool RequiresTwoFactorSetup { get; set; }

    public Capabilities.Capabilities Capabilities => capabilities.Current;

    /// <summary>Loads Capabilities after sign-in or restore. Failures leave the member-only shell in place.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await capabilities.RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    public Task SignOutAsync() => session.SignOutAsync();

    public bool CanOpen(string screenId) =>
        NavigationModel.Items.FirstOrDefault(i => i.ScreenId == screenId) is not { } item || item.IsVisible(capabilities.Current, flags);

    void Refresh()
    {
        var c = capabilities.Current;
        Personal = [.. NavigationModel.Visible(c, flags, NavSection.Personal)];
        Administration = [.. NavigationModel.Visible(c, flags, NavSection.Administration)];
        Developer = [.. NavigationModel.Visible(c, flags, NavSection.Developer)];
        Footer = [.. NavigationModel.Visible(c, flags, NavSection.Footer)];
        AccountEmail = c.Email;
        EnvironmentName = session.Environment.Name;
        IsProduction = session.Environment.IsProduction;
        IsTestMode = c.TestMode;
        RequiresTwoFactorSetup = c.IsStaff && !c.MfaEnabled;
    }

    /// <summary>COPY-AND-STATES → "API version notices (GLB-03)".</summary>
    internal static Banner? BannerFor(ApiVersionStatus status) => status.State switch
    {
        ApiVersionState.UpdateRequired => new("Update Substratal to continue. This version can no longer connect.", "Update", Blocking: true),
        ApiVersionState.UpdateAvailable => new(
            status.SunsetAt is { } sunset
                ? $"A new version of Substratal is available. This version stops working on {sunset.LocalDateTime:D}."
                : "A new version of Substratal is available.",
            "Update", Blocking: false),
        _ => null,
    };
}
