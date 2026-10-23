using System.Net;
using System.Net.Http.Headers;

namespace Substratal.Hub.Core.Auth;

/// <summary>
/// Attaches the platform access token, and on <c>401</c> refreshes once (single-flight, through
/// <see cref="SessionManager"/>) and replays the request (CLIENT-ARCHITECTURE §3 rule 1). Replaying is safe for
/// every method: authentication runs before any handler, so a request that got a 401 changed nothing.
/// </summary>
public sealed class BearerHandler(SessionManager session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await session.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null) return response;

        var fresh = await session.RefreshAsync(token, cancellationToken).ConfigureAwait(false);
        if (fresh is null) return response;

        response.Dispose();
        using var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var h in request.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in request.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        foreach (var o in request.Options) clone.Options.Set(new HttpRequestOptionsKey<object?>(o.Key), o.Value);
        return clone;
    }
}
