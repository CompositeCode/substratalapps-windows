using System.Text.Json.Serialization;

namespace Substratal.Hub.Core.Auth;

/// <summary>
/// Where the rotating refresh token lives between launches. The Windows app implements this with Credential Locker,
/// one entry per environment (CLIENT-ARCHITECTURE §3). Access tokens are never stored.
/// </summary>
public interface ITokenStore
{
    Task<string?> ReadRefreshTokenAsync(string environmentId, CancellationToken cancellationToken = default);
    Task WriteRefreshTokenAsync(string environmentId, string refreshToken, CancellationToken cancellationToken = default);
    Task DeleteAsync(string environmentId, CancellationToken cancellationToken = default);
}

/// <summary>An in-memory store, for tests and for "don't remember me".</summary>
public sealed class MemoryTokenStore : ITokenStore
{
    readonly Dictionary<string, string> tokens = new(StringComparer.Ordinal);
    readonly Lock gate = new();

    public Task<string?> ReadRefreshTokenAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(tokens.GetValueOrDefault(environmentId));
    }

    public Task WriteRefreshTokenAsync(string environmentId, string refreshToken, CancellationToken cancellationToken = default)
    {
        lock (gate) tokens[environmentId] = refreshToken;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        lock (gate) tokens.Remove(environmentId);
        return Task.CompletedTask;
    }
}

public abstract record SignInResult
{
    public sealed record SignedIn(string UserId, bool EmailVerified) : SignInResult;

    /// <summary>The account has TOTP on: continue at AUTH-02 with <see cref="MfaToken"/> (kept in memory only).</summary>
    public sealed record MfaRequired(string MfaToken, IReadOnlyList<string> Methods, DateTimeOffset ExpiresAt) : SignInResult;
}

public enum SessionEndReason
{
    SignedOut,
    /// <summary>Refresh failed with 401, or the server revoked the session.</summary>
    Revoked,
}

// Wire shapes for the auth endpoints, which are called directly rather than through the generated client so that
// refreshing a token never re-enters the authenticated pipeline.
internal sealed record LoginRequest(string Email, string Password, bool? TestMode);
internal sealed record MfaVerifyRequest(string MfaToken, string? Code, string? RecoveryCode);
internal sealed record RefreshRequest(string RefreshToken);
internal sealed record LogoutRequest(bool AllSessions);

/// <summary>Either an <c>AuthSession</c> or an <c>MfaChallenge</c>; the login response is a <c>oneOf</c> of the two.</summary>
internal sealed record AuthResponse(
    string? AccessToken,
    int? ExpiresIn,
    string? RefreshToken,
    string? SessionId,
    string? UserId,
    bool? EmailVerified,
    bool? MfaRequired,
    string? MfaToken,
    string[]? Methods)
{
    [JsonIgnore] public bool IsChallenge => MfaRequired == true && MfaToken is not null;
}
