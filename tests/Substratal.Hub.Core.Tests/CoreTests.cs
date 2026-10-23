using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Substratal.Hub.Core.Api;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Help;
using Substratal.Hub.Core.Navigation;
using Xunit;

namespace Substratal.Hub.Core.Tests;

/// <summary>A scripted transport: each call returns the next response and records the request.</summary>
sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
{
    int next;
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        var response = responses[Math.Min(next++, responses.Length - 1)](request);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, params (string, string)[] headers)
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (k, v) in headers) r.Headers.TryAddWithoutValidation(k, v);
        return r;
    }

    public static string Session(string access, string refresh) =>
        $$"""{"token_type":"Bearer","access_token":"{{access}}","expires_in":900,"refresh_token":"{{refresh}}","session_id":"ses_1","user_id":"usr_1","email_verified":true}""";
}

sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class ApiVersioningTests
{
    static HttpClient Client(StubHandler stub, ApiVersionStatus status) =>
        new(new ApiVersionHandler(status, [new ProductInfoHeaderValue("Substratal-Windows", "0.1.0")]) { InnerHandler = stub })
        { BaseAddress = ApiEnvironment.Production.BaseUrl };

    [Fact]
    public async Task Every_request_carries_the_version_header_at_the_host_root()
    {
        var stub = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "{}"));
        await Client(stub, new ApiVersionStatus()).GetAsync("users/me", TestContext.Current.CancellationToken);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("1", Assert.Single(request.Headers.GetValues("Substratal-Version")));
        Assert.Equal("https://api.substratalapps.com/users/me", request.RequestUri!.ToString());
        Assert.Contains("Substratal-Windows/0.1.0", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Deprecation_and_sunset_raise_update_available()
    {
        var status = new ApiVersionStatus();
        var stub = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "{}", ("Deprecation", "@1798761600"), ("Sunset", "Wed, 30 Jun 2027 00:00:00 GMT")));
        await Client(stub, status).GetAsync("users/me", TestContext.Current.CancellationToken);

        Assert.Equal(ApiVersionState.UpdateAvailable, status.State);
        Assert.Equal(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero), status.SunsetAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1798761600), status.DeprecatedAt);
    }

    [Fact]
    public async Task Unsupported_version_requires_an_update()
    {
        var status = new ApiVersionStatus();
        var stub = new StubHandler(_ => StubHandler.Json(HttpStatusCode.BadRequest, """{"error":{"code":"unsupported_api_version","message":"Version 1 was retired."}}"""));
        await Client(stub, status).GetAsync("users/me", TestContext.Current.CancellationToken);
        Assert.Equal(ApiVersionState.UpdateRequired, status.State);
    }

    [Theory]
    [InlineData("https://api.substratalapps.com/v1", "https://api.substratalapps.com/")]
    [InlineData("https://api.substratalapps.com/v1/", "https://api.substratalapps.com/")]
    [InlineData("http://localhost:8080/", "http://localhost:8080/")]
    [InlineData("https://staging.example.com/api/v2", "https://staging.example.com/api/")]
    public void Pasted_versioned_urls_are_reduced_to_the_root(string pasted, string expected) =>
        Assert.Equal(expected, ApiEnvironment.Root(new Uri(pasted)).ToString());
}

public class SessionTests
{
    static (SessionManager, StubHandler, MemoryTokenStore, ManualClock) Make(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var stub = new StubHandler(responses);
        var store = new MemoryTokenStore();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        return (new SessionManager(new HttpClient(stub) { BaseAddress = ApiEnvironment.Production.BaseUrl }, store, clock), stub, store, clock);
    }

    [Fact]
    public async Task Sign_in_persists_the_refresh_token_and_keeps_the_access_token_in_memory()
    {
        var (session, _, store, _) = Make(_ => StubHandler.Json(HttpStatusCode.OK, StubHandler.Session("at_1", "rt_1")));
        var result = await session.SignInAsync("dana@example.com", "correct horse battery", cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<SignInResult.SignedIn>(result);
        Assert.Equal("rt_1", await store.ReadRefreshTokenAsync("production", TestContext.Current.CancellationToken));
        Assert.Equal("at_1", await session.GetAccessTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_mfa_challenge_moves_to_two_factor()
    {
        var (session, _, store, _) = Make(_ => StubHandler.Json(HttpStatusCode.OK, """{"mfa_required":true,"mfa_token":"mfa_1","methods":["totp","recovery_code"],"expires_in":300}"""));
        var result = await session.SignInAsync("dana@example.com", "pw", cancellationToken: TestContext.Current.CancellationToken);

        var challenge = Assert.IsType<SignInResult.MfaRequired>(result);
        Assert.Equal("mfa_1", challenge.MfaToken);
        Assert.Null(await store.ReadRefreshTokenAsync("production", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_refreshes_hit_the_api_once()
    {
        var refreshes = 0;
        var (session, _, _, clock) = Make(
            _ => StubHandler.Json(HttpStatusCode.OK, StubHandler.Session("at_1", "rt_1")),
            _ => { Interlocked.Increment(ref refreshes); Thread.Sleep(50); return StubHandler.Json(HttpStatusCode.OK, StubHandler.Session("at_2", "rt_2")); });
        await session.SignInAsync("dana@example.com", "pw", cancellationToken: TestContext.Current.CancellationToken);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => session.RefreshAsync("at_1"))));

        Assert.Equal(1, refreshes);
        Assert.All(tokens, t => Assert.Equal("at_2", t));
    }

    [Fact]
    public async Task A_rejected_refresh_ends_the_session_and_forgets_the_token()
    {
        var (session, _, store, _) = Make(
            _ => StubHandler.Json(HttpStatusCode.OK, StubHandler.Session("at_1", "rt_1")),
            _ => StubHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"session_revoked","message":"Signed out elsewhere."}}"""));
        await session.SignInAsync("dana@example.com", "pw", cancellationToken: TestContext.Current.CancellationToken);
        SessionEndReason? ended = null;
        session.Ended += (_, r) => ended = r;

        Assert.Null(await session.RefreshAsync("at_1", TestContext.Current.CancellationToken));
        Assert.Equal(SessionEndReason.Revoked, ended);
        Assert.Null(await store.ReadRefreshTokenAsync("production", TestContext.Current.CancellationToken));
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task Bearer_handler_refreshes_once_on_401_and_replays()
    {
        var stub = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.OK, StubHandler.Session("at_1", "rt_1")),        // sign in
            _ => StubHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"unauthenticated","message":""}}"""), // GET with at_1
            _ => StubHandler.Json(HttpStatusCode.OK, StubHandler.Session("at_2", "rt_2")),        // refresh
            _ => StubHandler.Json(HttpStatusCode.OK, """{"id":"usr_1"}"""));                       // replay with at_2
        var http = new HttpClient(stub) { BaseAddress = ApiEnvironment.Production.BaseUrl };
        var session = new SessionManager(http, new MemoryTokenStore(), TimeProvider.System);
        await session.SignInAsync("dana@example.com", "pw", cancellationToken: TestContext.Current.CancellationToken);

        var api = new HttpClient(new BearerHandler(session) { InnerHandler = stub }) { BaseAddress = ApiEnvironment.Production.BaseUrl };
        var response = await api.GetAsync("users/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer at_2", stub.Requests[^1].Headers.Authorization!.ToString());
    }
}

public class SpecTests
{
    [Fact]
    public void Every_spec_screen_is_in_the_catalog() => Assert.Equal(75, ScreenCatalog.All.Count);

    [Fact]
    public void Every_navigation_item_names_a_real_screen() =>
        Assert.All(NavigationModel.Items, i => Assert.NotNull(ScreenCatalog.Find(i.ScreenId)));

    [Fact]
    public void Console_only_screens_are_not_in_the_windows_app() =>
        Assert.DoesNotContain(ScreenCatalog.All, s => s.Domain == "C");

    [Fact]
    public void A_member_sees_only_personal_items_and_the_footer()
    {
        var flags = new FeatureFlags();
        var c = Capabilities.Capabilities.None;
        Assert.False(NavigationModel.SectionVisible(c, flags, NavSection.Administration));
        Assert.False(NavigationModel.SectionVisible(c, flags, NavSection.Developer));
        Assert.Contains(NavigationModel.Visible(c, flags, NavSection.Footer), i => i.ScreenId == "SET-01");
    }

    [Fact]
    public void Support_sees_users_access_and_audit_but_not_roles_management_or_keys()
    {
        var support = Capabilities.Capabilities.None with
        {
            Permissions = new HashSet<string> { "users.list", "entitlements.manage", "audit.view", "tenants.manage", "billing.manage" },
        };
        var admin = NavigationModel.Visible(support, new FeatureFlags(), NavSection.Administration).Select(i => i.ScreenId).ToList();
        Assert.Equal(["USR-01", "ACX-05", "ROL-01", "AUD-01"], admin);
        Assert.Equal(["BIL-04"], NavigationModel.Visible(support, new FeatureFlags(), NavSection.Developer).Select(i => i.ScreenId));
    }

    [Fact]
    public void Help_links_point_at_generated_screen_pages() =>
        Assert.Equal("https://compositecode.github.io/substratalapps-applications-specs/help/screens/usr-01.html", HelpLinks.ForScreen("USR-01").ToString());
}

public class ErrorCopyTests
{
    [Fact]
    public void Builder_notes_are_stripped_and_placeholders_filled()
    {
        var text = ErrorCopy.For(new ApiError(429, "rate_limited", "", "req_1", null), TimeSpan.FromSeconds(12.2))!;
        Assert.Equal("Too many attempts. Try again in 13s.", text.Message);
    }

    [Fact]
    public void Action_labels_are_split_from_the_message()
    {
        var text = ErrorCopy.For(new ApiError(409, "version_conflict", "", null, null))!;
        Assert.Equal("This changed since you opened it.", text.Message);
        Assert.Equal(["Reload", "Re-apply"], text.Actions);
    }

    [Fact]
    public void Request_ids_stay_in_the_message()
    {
        var text = ErrorCopy.For(new ApiError(500, "internal_error", "", "req_9", null))!;
        Assert.Equal("Something went wrong on our side. Request ID req_9", text.Message);
        Assert.Empty(text.Actions);
    }

    [Fact]
    public void Never_shown_codes_return_null() => Assert.Null(ErrorCopy.For(new ApiError(501, "not_implemented", "", null, null)));
}
