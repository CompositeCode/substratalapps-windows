using Substratal.Hub.Core.Capabilities;

namespace Substratal.Hub.Core.Navigation;

/// <summary>One screen from <c>windows/SCREENS.md</c>. The full list is generated into <see cref="ScreenCatalog.All"/>.</summary>
public sealed record Screen(
    string Id,
    string Title,
    string Domain,
    string DomainTitle,
    string Route,
    string Archetype,
    string Phase,
    string Who,
    string Purpose,
    string WindowsContainer)
{
    /// <summary>Screens reached from the NavigationView, a drill-in, or a deep link (not modals, overlays or handoffs).</summary>
    public bool IsPage => Route.StartsWith('/');
    public bool IsOutsideShell => Archetype == "auth";
}

public static partial class ScreenCatalog
{
    // Lazy: All lives in another partial file, and static initializers across partial files run in no defined order.
    static readonly Lazy<Dictionary<string, Screen>> ById = new(() => All!.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase));

    public static Screen Get(string id) => ById.Value.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException($"No screen {id} in windows/SCREENS.md.");
    public static Screen? Find(string id) => ById.Value.GetValueOrDefault(id);
    public static IEnumerable<Screen> InDomain(string domain) => All.Where(s => s.Domain == domain);
}

public enum NavSection { Personal, Administration, Developer, Footer }

/// <summary>
/// One NavigationView item. <paramref name="Glyph"/> is a Segoe Fluent Icons code point for the meaning in
/// INFORMATION-ARCHITECTURE §5 (Fluent column). <paramref name="Shortcut"/> is from PLATFORM-SPEC §3.
/// </summary>
public sealed record NavItem(string ScreenId, string Label, NavSection Section, string Glyph, string Shortcut, Func<Capabilities.Capabilities, FeatureFlags, bool> IsVisible)
{
    public Screen Screen => ScreenCatalog.Get(ScreenId);
}

/// <summary>
/// The Windows navigation tree (INFORMATION-ARCHITECTURE §1, PLATFORM-SPEC §2). The console-only sections
/// (Operations, Reports, Integrations) aren't here: native apps skip them (PRODUCT-SPEC §2a, OPEN-QUESTIONS #27).
/// </summary>
public static class NavigationModel
{
    static bool Always(Capabilities.Capabilities c, FeatureFlags f) => true;

    public static IReadOnlyList<NavItem> Items { get; } =
    [
        new("APP-01", "Home", NavSection.Personal, "", "Ctrl+1", Always),
        new("NTF-01", "Inbox", NavSection.Personal, "", "Ctrl+2", (c, f) => f.Notifications),
        new("ORG-01", "Organizations", NavSection.Personal, "", "Ctrl+3", (c, f) => f.Organizations),

        new("USR-01", "Users", NavSection.Administration, "", "Ctrl+Shift+1", (c, f) => c.CanListUsers),
        new("ACX-05", "Access", NavSection.Administration, "", "Ctrl+Shift+2", (c, f) => c.CanManageEntitlements || c.OwnsApps),
        new("ROL-01", "Roles", NavSection.Administration, "", "Ctrl+Shift+3", (c, f) => c.CanListUsers || c.CanManageRoles),
        new("AUD-01", "Audit Log", NavSection.Administration, "", "Ctrl+Shift+4", (c, f) => c.CanViewAudit),
        new("NTF-06", "Send notification", NavSection.Administration, "", "", (c, f) => c.CanSendNotifications && f.SendNotifications),

        new("APL-01", "Applications", NavSection.Developer, "", "Ctrl+Alt+1", (c, f) => c.OwnsApps || c.CanManageApplications),
        new("KEY-01", "API Keys", NavSection.Developer, "", "Ctrl+Alt+2", (c, f) => c.OwnsApps || c.CanManageApiKeys),
        new("WHK-01", "Webhooks", NavSection.Developer, "", "Ctrl+Alt+3", (c, f) => (c.OwnsApps || c.CanManageWebhooks) && f.Webhooks),
        new("BIL-04", "Tenant", NavSection.Developer, "", "Ctrl+Alt+4", (c, f) => c.OwnsTenants || c.CanManageTenants),

        new("ACC-01", "Account", NavSection.Footer, "", "Ctrl+4", Always),
        new("BIL-01", "Plan & Billing", NavSection.Footer, "", "Ctrl+5", (c, f) => f.Billing),
        new("SET-01", "Settings", NavSection.Footer, "", "Ctrl+6", Always),
    ];

    public static IEnumerable<NavItem> Visible(Capabilities.Capabilities capabilities, FeatureFlags flags, NavSection section) =>
        Items.Where(i => i.Section == section && i.IsVisible(capabilities, flags));

    /// <summary>The Administration header shows when any of its items does (IA §2).</summary>
    public static bool SectionVisible(Capabilities.Capabilities capabilities, FeatureFlags flags, NavSection section) =>
        Visible(capabilities, flags, section).Any();

    /// <summary>The landing screen after sign-in: Home for everyone (APP-01).</summary>
    public const string Landing = "APP-01";
}

/// <summary>
/// Client feature flags (CLIENT-ARCHITECTURE §10). Every Hub operation is built as of API v1.8.0, so the defaults are
/// on; a <c>404</c> or <c>501</c> from a gated endpoint turns its flag off for the session. Remote config replaces
/// these once the API serves flags ([api-gaps] G11, CompositeCode/substratalapps.com#48).
/// </summary>
public sealed class FeatureFlags
{
    public bool Organizations { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool SendNotifications { get; set; } = true;
    public bool Webhooks { get; set; } = true;
    public bool Billing { get; set; } = true;
    public bool AppSettings { get; set; } = true;
    public bool AppRoles { get; set; } = true;
    public bool TierChanges { get; set; } = true;
    public bool AuditExport { get; set; } = true;
    public bool Sso { get; set; }
    public bool Passkeys { get; set; }
    public bool HostedLaunch { get; set; }
}
