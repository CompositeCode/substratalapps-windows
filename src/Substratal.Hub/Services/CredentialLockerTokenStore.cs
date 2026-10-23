using Substratal.Hub.Core.Auth;
using Windows.Security.Credentials;

namespace Substratal.Hub.Services;

/// <summary>
/// The refresh token in Windows Credential Locker, one entry per environment (PLATFORM-SPEC §1, CLIENT-ARCHITECTURE §3).
/// Credential Locker is per Windows user and encrypted with their credentials; nothing is written to disk by the app.
/// </summary>
public sealed class CredentialLockerTokenStore : ITokenStore
{
    const string UserName = "refresh_token";
    readonly PasswordVault vault = new();

    static string Resource(string environmentId) => $"Substratal Hub ({environmentId})";

    public Task<string?> ReadRefreshTokenAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var credential = vault.Retrieve(Resource(environmentId), UserName);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception e) when (e.HResult == unchecked((int)0x80070490)) // ERROR_NOT_FOUND
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task WriteRefreshTokenAsync(string environmentId, string refreshToken, CancellationToken cancellationToken = default)
    {
        // Add replaces an existing credential with the same resource and user name.
        vault.Add(new PasswordCredential(Resource(environmentId), UserName, refreshToken));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            vault.Remove(vault.Retrieve(Resource(environmentId), UserName));
        }
        catch (Exception e) when (e.HResult == unchecked((int)0x80070490))
        {
        }
        return Task.CompletedTask;
    }
}
