using System.Globalization;
using System.Net.Http.Headers;

namespace Substratal.Hub.Core.Api;

/// <summary>The API version this build is written against (CLIENT-ARCHITECTURE §4.1).</summary>
public static class ApiVersion
{
    public const string HeaderName = "Substratal-Version";

    /// <summary>
    /// The <c>Substratal-Version</c> value every request sends. Raise it only together with regenerating the
    /// client from that version's <c>openapi.yaml</c> (see <c>api/README.md</c>).
    /// </summary>
    public const string Current = "1";
}

/// <summary>Whether the API still supports the version this build sends. Drives the GLB-03 update states.</summary>
public enum ApiVersionState
{
    Supported,
    /// <summary>The API answered with <c>Deprecation</c> (RFC 9745), and usually <c>Sunset</c> (RFC 8594).</summary>
    UpdateAvailable,
    /// <summary>The API answered <c>400 unsupported_api_version</c>: this build can't talk to it any more.</summary>
    UpdateRequired,
}

/// <summary>Shared, observable version status. One instance per app, fed by <see cref="ApiVersionHandler"/>.</summary>
public sealed class ApiVersionStatus
{
    public ApiVersionState State { get; private set; } = ApiVersionState.Supported;
    public DateTimeOffset? DeprecatedAt { get; private set; }
    public DateTimeOffset? SunsetAt { get; private set; }

    public event EventHandler? Changed;

    internal void ReportDeprecated(DateTimeOffset? deprecatedAt, DateTimeOffset? sunsetAt)
    {
        if (State == ApiVersionState.UpdateRequired) return;
        if (State == ApiVersionState.UpdateAvailable && SunsetAt == sunsetAt && DeprecatedAt == deprecatedAt) return;
        (State, DeprecatedAt, SunsetAt) = (ApiVersionState.UpdateAvailable, deprecatedAt, sunsetAt);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ReportUnsupported()
    {
        if (State == ApiVersionState.UpdateRequired) return;
        State = ApiVersionState.UpdateRequired;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Parses a <c>Deprecation</c> value. RFC 9745 sends a structured-field date (<c>@1767225600</c>);
    /// an HTTP-date is accepted too.
    /// </summary>
    internal static DateTimeOffset? ParseDeprecation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value[0] == '@' && long.TryParse(value.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        return ParseHttpDate(value);
    }

    internal static DateTimeOffset? ParseHttpDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}

/// <summary>
/// Adds <c>Substratal-Version</c> and the app's <c>User-Agent</c> to every request, including the auth calls,
/// and watches every response for deprecation. Requests without the header fail with
/// <c>400 api_version_required</c> before authentication even runs, so this sits outermost in the pipeline.
/// </summary>
public sealed class ApiVersionHandler(ApiVersionStatus status, IReadOnlyList<ProductInfoHeaderValue> userAgent) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ApiVersion.HeaderName);
        request.Headers.TryAddWithoutValidation(ApiVersion.HeaderName, ApiVersion.Current);
        if (request.Headers.UserAgent.Count == 0)
            foreach (var product in userAgent) request.Headers.UserAgent.Add(product);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Headers.TryGetValues("Deprecation", out var deprecation))
        {
            var sunset = response.Headers.TryGetValues("Sunset", out var s) ? ApiVersionStatus.ParseHttpDate(s.FirstOrDefault()) : null;
            status.ReportDeprecated(ApiVersionStatus.ParseDeprecation(deprecation.FirstOrDefault()), sunset);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && response.Content is not null)
        {
            var error = await ApiError.TryReadAsync(response, cancellationToken).ConfigureAwait(false);
            if (error?.Code == "unsupported_api_version") status.ReportUnsupported();
        }

        return response;
    }
}
