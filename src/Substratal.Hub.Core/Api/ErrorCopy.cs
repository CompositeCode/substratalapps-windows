using System.Text.RegularExpressions;

namespace Substratal.Hub.Core.Api;

/// <summary>What to show for an error: the sentence, and the action labels the spec pairs with it.</summary>
public sealed record ErrorText(string Message, IReadOnlyList<string> Actions, string? RequestId);

/// <summary>
/// Maps an <see cref="ApiError"/> to the copy in COPY-AND-STATES (generated into <c>ErrorCopy.g.cs</c>).
/// Spec cells hold the message, then any action labels ("Reload · Re-apply"), then builder notes in parentheses
/// ("(client bug: …)"); only the first two reach the screen.
/// </summary>
public static partial class ErrorCopy
{
    const string Fallback = "Something went wrong on our side. Request ID {id}";

    /// <summary>
    /// Null when the spec says the code is never shown (<c>not_implemented</c>: hide the feature) or is handled
    /// in place (<c>validation_failed</c>: per-field messages from <see cref="ApiError.FieldErrors"/>).
    /// </summary>
    public static ErrorText? For(ApiError error, TimeSpan? retryAfter = null, IReadOnlyDictionary<string, string>? values = null)
    {
        var raw = Table.TryGetValue(error.Code, out var entry) ? entry.Message : Fallback;
        if (raw.StartsWith('(')) return null;

        var text = Notes().Replace(raw, "").Trim();
        var (message, actions) = SplitActions(text);
        message = message
            .Replace("{id}", error.RequestId ?? "unavailable", StringComparison.Ordinal)
            .Replace("{n}", ((int)Math.Ceiling((retryAfter ?? TimeSpan.Zero).TotalSeconds)).ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal);
        if (values is not null)
            foreach (var (k, v) in values) message = message.Replace("{" + k + "}", v, StringComparison.Ordinal);
        return new ErrorText(message, actions, error.RequestId);
    }

    public static bool IsKnown(string code) => Table.ContainsKey(code);

    // "Message. Action · Action": the actions are what follows the last sentence end, when it has no placeholder.
    static (string Message, IReadOnlyList<string> Actions) SplitActions(string text)
    {
        var end = text.LastIndexOfAny(['.', '?', '!']);
        if (end < 0 || end == text.Length - 1) return (text, []);
        var tail = text[(end + 1)..].Trim();
        if (tail.Length == 0 || tail.Contains('{')) return (text, []);
        return (text[..(end + 1)], tail.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"\s*\([^()]*\)")]
    private static partial Regex Notes();
}
