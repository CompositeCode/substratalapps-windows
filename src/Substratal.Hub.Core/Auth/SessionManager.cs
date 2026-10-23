using System.Net;
using System.Net.Http.Json;
using Substratal.Hub.Core.Api;

namespace Substratal.Hub.Core.Auth;

/// <summary>
/// Owns the platform session: sign-in, MFA, restore on launch, refresh and sign-out (CLIENT-ARCHITECTURE §3).
/// The access token lives only in memory. The refresh token is rotating and single-use, so refreshing is
/// single-flight: reusing a rotated token revokes the whole session and raises <c>security.session_reuse_detected</c>.
/// </summary>
public sealed class SessionManager(HttpClient authHttp, ITokenStore store, TimeProvider clock)
{
    /// <summary>Refresh this long before the access token expires (§3 rule 2).</summary>
    public static readonly TimeSpan RefreshAhead = TimeSpan.FromSeconds(60);

    readonly SemaphoreSlim refreshGate = new(1, 1);
    string? accessToken;
    DateTimeOffset accessExpiresAt;

    public ApiEnvironment Environment { get; private set; } = ApiEnvironment.Production;
    public string? UserId { get; private set; }
    public string? SessionId { get; private set; }
    public bool IsSignedIn => accessToken is not null;

    public event EventHandler<SessionEndReason>? Ended;
    public event EventHandler<ApiEnvironment>? EnvironmentChanged;

    /// <summary>Choose the environment before signing in. Switching signs the current session out locally.</summary>
    public void UseEnvironment(ApiEnvironment environment)
    {
        if (environment == Environment) return;
        ClearMemory();
        Environment = environment;
        authHttp.BaseAddress = environment.BaseUrl;
        EnvironmentChanged?.Invoke(this, environment);
    }

    public async Task<SignInResult> SignInAsync(string email, string password, bool testMode = false, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("auth/login", new LoginRequest(email.Trim(), password, testMode ? true : null), ApiJson.Default.LoginRequest, cancellationToken).ConfigureAwait(false);
        if (response.IsChallenge)
            return new SignInResult.MfaRequired(response.MfaToken!, response.Methods ?? [], clock.GetUtcNow().AddSeconds(response.ExpiresIn ?? 300));
        return await AcceptAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SignInResult.SignedIn> VerifyMfaAsync(string mfaToken, string? code, string? recoveryCode, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("auth/mfa/verify", new MfaVerifyRequest(mfaToken, code, recoveryCode), ApiJson.Default.MfaVerifyRequest, cancellationToken).ConfigureAwait(false);
        return await AcceptAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>On launch: exchange a stored refresh token for a session. False means show AUTH-01.</summary>
    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        var stored = await store.ReadRefreshTokenAsync(Environment.Id, cancellationToken).ConfigureAwait(false);
        if (stored is null) return false;
        return await RefreshAsync(staleAccessToken: null, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>The current access token, refreshed first if it's within <see cref="RefreshAhead"/> of expiring.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = accessToken;
        if (token is not null && clock.GetUtcNow() < accessExpiresAt - RefreshAhead) return token;
        return await RefreshAsync(token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes once, however many callers ask at the same time. A caller passes the token it saw fail; if another
    /// caller has already replaced it, that new token is returned without a second refresh. Null means the session ended.
    /// </summary>
    public async Task<string?> RefreshAsync(string? staleAccessToken, CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (accessToken is not null && accessToken != staleAccessToken && clock.GetUtcNow() < accessExpiresAt - RefreshAhead)
                return accessToken;

            var refreshToken = await store.ReadRefreshTokenAsync(Environment.Id, cancellationToken).ConfigureAwait(false);
            if (refreshToken is null)
            {
                End(SessionEndReason.Revoked);
                return null;
            }

            AuthResponse response;
            try
            {
                response = await PostAsync("auth/token/refresh", new RefreshRequest(refreshToken), ApiJson.Default.RefreshRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (HubApiException e) when (e.Error.Status is 401 or 403)
            {
                await store.DeleteAsync(Environment.Id, cancellationToken).ConfigureAwait(false);
                End(SessionEndReason.Revoked);
                return null;
            }

            await AcceptAsync(response, cancellationToken).ConfigureAwait(false);
            return accessToken;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>Sign out: revoke on the server (best effort), then forget everything locally (§3 rule 4).</summary>
    public async Task SignOutAsync(bool everywhere = false, CancellationToken cancellationToken = default)
    {
        var token = accessToken;
        if (token is not null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "auth/logout")
                {
                    Content = JsonContent.Create(new LogoutRequest(everywhere), ApiJson.Default.LogoutRequest),
                };
                request.Headers.Authorization = new("Bearer", token);
                using var _ = await authHttp.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Offline: the local wipe below still signs this device out; the session expires server-side.
            }
        }
        await store.DeleteAsync(Environment.Id, cancellationToken).ConfigureAwait(false);
        End(SessionEndReason.SignedOut);
    }

    async Task<SignInResult.SignedIn> AcceptAsync(AuthResponse response, CancellationToken cancellationToken)
    {
        if (response.AccessToken is null || response.RefreshToken is null)
            throw new InvalidOperationException("The API returned a session without tokens.");

        // Persist the rotated refresh token before the new access token is used (§3): if the app dies in between,
        // the next launch still holds a token the server will accept.
        await store.WriteRefreshTokenAsync(Environment.Id, response.RefreshToken, cancellationToken).ConfigureAwait(false);
        accessToken = response.AccessToken;
        accessExpiresAt = clock.GetUtcNow().AddSeconds(response.ExpiresIn ?? 900);
        UserId = response.UserId;
        SessionId = response.SessionId;
        return new SignInResult.SignedIn(response.UserId ?? "", response.EmailVerified ?? false);
    }

    async Task<AuthResponse> PostAsync<T>(string path, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        using var response = await authHttp.PostAsync(path, JsonContent.Create(body, type), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ApiError.TryReadAsync(response, cancellationToken).ConfigureAwait(false)
                ?? new ApiError((int)response.StatusCode, response.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "http_error", response.ReasonPhrase ?? "", null, null);
            throw new HubApiException(error);
        }
        return await response.Content.ReadFromJsonAsync(ApiJson.Default.AuthResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Empty response from {path}.");
    }

    void End(SessionEndReason reason)
    {
        var wasSignedIn = IsSignedIn;
        ClearMemory();
        if (wasSignedIn || reason == SessionEndReason.Revoked) Ended?.Invoke(this, reason);
    }

    void ClearMemory()
    {
        accessToken = null;
        accessExpiresAt = default;
        UserId = null;
        SessionId = null;
    }
}
