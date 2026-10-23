namespace Substratal.Hub.Core.Api;

/// <summary>
/// Where the app sends requests. The base URL is the host root: the API version is the
/// <c>Substratal-Version</c> request header, never a path segment (CLIENT-ARCHITECTURE §4.1).
/// Paths are identical in every API version, so switching versions never changes a URL.
/// </summary>
public sealed record ApiEnvironment(string Id, string Name, Uri BaseUrl, bool IsProduction)
{
    public static readonly ApiEnvironment Production =
        new("production", "Production", new Uri("https://api.substratalapps.com"), IsProduction: true);

    /// <summary>The API repo's devserver (<c>make run</c>), with every route on one port.</summary>
    public static readonly ApiEnvironment Local =
        new("local", "Local devserver", new Uri("http://localhost:8080"), IsProduction: false);

    public static IReadOnlyList<ApiEnvironment> BuiltIn { get; } = [Production, Local];

    public static ApiEnvironment Custom(Uri baseUrl) => new("custom", "Custom", Root(baseUrl), IsProduction: false);

    /// <summary>
    /// Strips a trailing <c>/v1</c> (or any <c>/v&lt;n&gt;</c>) from a pasted URL. Older docs used versioned paths,
    /// which now return <c>404 not_found</c> with no redirect.
    /// </summary>
    public static Uri Root(Uri url)
    {
        var path = url.AbsolutePath.TrimEnd('/');
        var last = path.LastIndexOf('/');
        var segment = last >= 0 ? path[(last + 1)..] : path;
        if (segment.Length > 1 && segment[0] == 'v' && segment[1..].All(char.IsAsciiDigit))
            path = path[..Math.Max(last, 0)];
        return new UriBuilder(url) { Path = path.Length == 0 ? "/" : path + "/", Query = "", Fragment = "" }.Uri;
    }
}
