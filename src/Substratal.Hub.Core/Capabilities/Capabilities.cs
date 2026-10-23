using Substratal.Hub.Core.Api.Generated;
using Substratal.Hub.Core.Api.Generated.Models;

namespace Substratal.Hub.Core.Capabilities;

/// <summary>
/// What the signed-in person can see and do (CLIENT-ARCHITECTURE §5). Navigation and every action read only from
/// this; the server is always the authority, and a <c>403</c> triggers a background refresh.
/// </summary>
public sealed record Capabilities(
    string UserId,
    string Email,
    bool MfaEnabled,
    bool EmailVerified,
    bool TestMode,
    IReadOnlySet<string> PlatformRoleIds,
    IReadOnlySet<string> Permissions,
    IReadOnlyList<string> OwnedApplicationIds,
    IReadOnlyList<string> OwnedTenantIds,
    IReadOnlyList<string> AdminOfOrganizationIds)
{
    public bool Has(string permission) => Permissions.Contains(permission);

    public bool CanListUsers => Has("users.list");
    public bool CanManageUsers => Has("users.manage");
    public bool CanManageEntitlements => Has("entitlements.manage");
    public bool CanManageRoles => Has("roles.manage");
    public bool CanViewAudit => Has("audit.view");
    public bool CanManageApplications => Has("applications.manage");
    public bool CanManageApiKeys => Has("api_keys.manage");
    public bool CanManageWebhooks => Has("webhooks.manage");
    public bool CanManageTenants => Has("tenants.manage");
    public bool CanManageBilling => Has("billing.manage");
    public bool CanSendNotifications => Has("notifications.send");
    public bool CanManageOrganizations => Has("organizations.manage");

    public bool OwnsApps => OwnedApplicationIds.Count > 0;
    public bool OwnsTenants => OwnedTenantIds.Count > 0;

    /// <summary>Any platform role that grants something. Staff accounts must have two-factor on (PLAN.md §3.1).</summary>
    public bool IsStaff => Permissions.Count > 0;

    /// <summary>A signed-out or not-yet-loaded state: sees only what needs no permission.</summary>
    public static Capabilities None { get; } = new("", "", false, false, false,
        new HashSet<string>(), new HashSet<string>(), [], [], []);
}

/// <summary>Computes <see cref="Capabilities"/> from the six reads in CLIENT-ARCHITECTURE §5, in parallel.</summary>
public sealed class CapabilitiesService(SubstratalApiClient api)
{
    /// <summary>The platform role catalog changes rarely; cache it for an hour (§5).</summary>
    static readonly TimeSpan RoleCacheLifetime = TimeSpan.FromHours(1);

    Dictionary<string, IReadOnlyList<string>>? roleCache;
    DateTimeOffset roleCacheAt;

    public Capabilities Current { get; private set; } = Capabilities.None;
    public event EventHandler? Changed;

    public async Task<Capabilities> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var meTask = api.Users["me"].GetAsync(cancellationToken: cancellationToken);
        var rolesTask = api.Users["me"].Roles.GetAsync(r => r.QueryParameters.Limit = 100, cancellationToken);
        var catalogTask = PlatformRoleCatalogAsync(cancellationToken);
        var appsTask = api.Applications.GetAsync(r => { r.QueryParameters.Owned = true; r.QueryParameters.Limit = 100; }, cancellationToken);
        var tenantsTask = api.Tenants.GetAsync(r => r.QueryParameters.Limit = 100, cancellationToken);
        var orgsTask = api.Organizations.GetAsync(r => r.QueryParameters.Limit = 100, cancellationToken);
        await Task.WhenAll(meTask, rolesTask, catalogTask, appsTask, tenantsTask, orgsTask).ConfigureAwait(false);

        var me = await meTask.ConfigureAwait(false) ?? throw new InvalidOperationException("GET /users/me returned nothing.");
        var catalog = await catalogTask.ConfigureAwait(false);
        var platformRoles = ((await rolesTask.ConfigureAwait(false))?.Data ?? [])
            .Where(a => a.ApplicationId is null && a.RoleId is not null)
            .Select(a => a.RoleId!)
            .ToHashSet(StringComparer.Ordinal);
        var permissions = platformRoles
            .SelectMany(id => catalog.GetValueOrDefault(id) ?? [])
            .ToHashSet(StringComparer.Ordinal);

        var userId = me.Id ?? "";
        var tenants = (await tenantsTask.ConfigureAwait(false))?.Data ?? [];
        var orgs = (await orgsTask.ConfigureAwait(false))?.Data ?? [];

        Current = new Capabilities(
            userId,
            me.Email ?? "",
            me.MfaEnabled ?? false,
            me.EmailVerified ?? false,
            me.TestMode ?? false,
            platformRoles,
            permissions,
            ((await appsTask.ConfigureAwait(false))?.Data ?? []).Select(a => a.Id).OfType<string>().ToList(),
            // A staff caller with tenants.manage lists every Tenant; only the ones they own count as theirs.
            tenants.Where(t => t.OwnerUserId == userId).Select(t => t.Id).OfType<string>().ToList(),
            orgs.Where(o => o.MyRole == OrganizationMemberRole.Org_admin).Select(o => o.Id).OfType<string>().ToList());
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public void Clear()
    {
        Current = Capabilities.None;
        roleCache = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    async Task<Dictionary<string, IReadOnlyList<string>>> PlatformRoleCatalogAsync(CancellationToken cancellationToken)
    {
        if (roleCache is not null && DateTimeOffset.UtcNow - roleCacheAt < RoleCacheLifetime) return roleCache;
        RolePage? page;
        try
        {
            page = await api.Roles.GetAsync(r => { r.QueryParameters.Scope = "platform"; r.QueryParameters.Limit = 100; }, cancellationToken).ConfigureAwait(false);
        }
        catch (Error e) when (e.ResponseStatusCode == 403)
        {
            // A member with no admin permissions can't read the role catalog. That's the expected answer, not a failure.
            page = null;
        }
        roleCache = (page?.Data ?? [])
            .Where(r => r.Id is not null)
            .ToDictionary(r => r.Id!, r => (IReadOnlyList<string>)(r.Permissions ?? []), StringComparer.Ordinal);
        roleCacheAt = DateTimeOffset.UtcNow;
        return roleCache;
    }
}
