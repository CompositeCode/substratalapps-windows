using System.Text.Json;
using System.Text.Json.Serialization;

namespace Substratal.Hub.Core.Api;

/// <summary>
/// The API's error envelope, <c>{"error":{"code","message","details"}}</c> (Conventions → Errors).
/// Screens map <see cref="Code"/> to copy from COPY-AND-STATES and show <see cref="Message"/> only inside a
/// "Details" disclosure, next to <see cref="RequestId"/>.
/// </summary>
public sealed record ApiError(int Status, string Code, string Message, string? RequestId, JsonElement? Details)
{
    /// <summary>Field-level validation messages from <c>details.fields</c>, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors
    {
        get
        {
            if (Details is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("fields", out var fields)) return Empty;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (fields.ValueKind == JsonValueKind.Object)
                foreach (var f in fields.EnumerateObject())
                    result[f.Name] = f.Value.ValueKind == JsonValueKind.String ? f.Value.GetString()! : f.Value.ToString();
            else if (fields.ValueKind == JsonValueKind.Array)
                foreach (var f in fields.EnumerateArray())
                    if (f.TryGetProperty("field", out var name))
                        result[name.GetString() ?? ""] = f.TryGetProperty("code", out var c) ? c.GetString() ?? "" : f.ToString();
            return result;
        }
    }

    static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    /// <summary>From the generated client's exception, keeping the request id from the response headers.</summary>
    public static ApiError From(Generated.Models.Error e)
    {
        var requestId = e.ResponseHeaders.TryGetValue("X-Request-Id", out var ids) ? ids.FirstOrDefault() : null;
        return new ApiError(e.ResponseStatusCode, e.ErrorProp?.Code ?? "unknown", e.ErrorProp?.Message ?? "", requestId, null);
    }

    /// <summary>Reads the envelope from a failed response without consuming it for later readers.</summary>
    public static async Task<ApiError?> TryReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode || response.Content is null) return null;
        try
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync(stream, ApiJson.Default.ErrorEnvelope, cancellationToken).ConfigureAwait(false);
            if (stream.CanSeek) stream.Position = 0;
            if (envelope?.Error is not { } e) return null;
            var requestId = response.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.FirstOrDefault() : null;
            return new ApiError((int)response.StatusCode, e.Code ?? "unknown", e.Message ?? "", requestId, e.Details);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Thrown by the hand-written auth calls. Generated calls throw Kiota's <c>Error</c> model instead.</summary>
public sealed class HubApiException(ApiError error) : Exception($"{error.Status} {error.Code}: {error.Message}")
{
    public ApiError Error { get; } = error;
}

internal sealed record ErrorEnvelope(ErrorBody? Error);
internal sealed record ErrorBody(string? Code, string? Message, JsonElement? Details);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(Auth.LoginRequest))]
[JsonSerializable(typeof(Auth.MfaVerifyRequest))]
[JsonSerializable(typeof(Auth.RefreshRequest))]
[JsonSerializable(typeof(Auth.LogoutRequest))]
[JsonSerializable(typeof(Auth.AuthResponse))]
internal sealed partial class ApiJson : JsonSerializerContext;
