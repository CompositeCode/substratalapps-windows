namespace Substratal.Hub.Core.Help;

/// <summary>
/// Links into the Substratal Hub help center, the one documentation reference every Hub app shares. It's built from
/// CompositeCode/substratalapps-applications-specs (<c>help/</c> and <c>site/help.mjs</c>) and rebuilt whenever this
/// repository merges to main (<c>.github/workflows/docs-dispatch.yml</c>).
/// </summary>
public static class HelpLinks
{
    public static Uri Root { get; } = new("https://compositecode.github.io/substratalapps-applications-specs/help/");

    /// <summary>The page behind each screen's <b>?</b> button: generated per screen, IDs lower-cased.</summary>
    public static Uri ForScreen(string screenId) => new(Root, $"screens/{screenId.ToLowerInvariant()}.html");

    public static Uri Guide(string slug) => new(Root, $"guides/{slug}.html");
    public static Uri Tutorial(string slug) => new(Root, $"tutorials/{slug}.html");

    /// <summary>The Windows app's own guide: install, update, sign-in, notifications and shortcuts.</summary>
    public static Uri WindowsApp { get; } = Guide("windows-app");

    public static Uri Faq { get; } = new(Root, "faq.html");
    public static Uri WhatsNew { get; } = new(Root, "whats-new.html");

    /// <summary>The interactive reference for the API version this build sends (<c>Substratal-Version</c>).</summary>
    public static Uri ApiReference { get; } = new(Root, $"api/v{Api.ApiVersion.Current}/index.html");

    /// <summary>Full-text search across guides, screens and the API reference.</summary>
    public static Uri Search(string query) => new(Root, $"search.html?q={Uri.EscapeDataString(query)}");
}
