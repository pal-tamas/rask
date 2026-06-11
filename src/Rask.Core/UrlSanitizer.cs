namespace Rask.Core;

/// <summary>
///     Opt-out marker for the URL-attribute scheme sanitization Rask applies by default to
///     <c>href</c>/<c>src</c>/<c>action</c>/<c>cite</c>/etc. Wrap a value you fully control in
///     <see cref="Trusted" /> to emit it verbatim (it is still HTML-encoded). Use only for URLs
///     that are not attacker-influenced — e.g. a hard-coded <c>javascript:void(0)</c> sentinel.
/// </summary>
public static class RaskUrl
{
    // A sentinel prefix that the sanitizer recognizes and strips before output; a real URL would
    // never start with it. If it ever leaked unstripped it would be HTML-encoded harmlessly.
    internal const string TrustedPrefix = "rask-trusted:";

    /// <summary>
    ///     Marks <paramref name="url" /> as trusted so the next URL-attribute emit skips scheme
    ///     sanitization. The value is still HTML-encoded. Only use for non-attacker-controlled URLs.
    /// </summary>
    public static string Trusted(string url) => TrustedPrefix + (url ?? string.Empty);
}

/// <summary>
///     Neutralizes dangerous URL schemes (<c>javascript:</c>, <c>vbscript:</c>, and — outside
///     media attributes — <c>data:</c>) before they reach <c>href</c>/<c>src</c>/<c>action</c>/etc.
///     Output encoding alone (<see cref="HtmlSerializer.AppendEncoded" />) does not stop these:
///     <c>&lt;a href="javascript:..."&gt;</c> executes on click. Detection mirrors the WHATWG URL
///     parser's leniencies (leading C0/space stripped, embedded tab/newline/control removed,
///     scheme compared case-insensitively) so obfuscation like <c>java&#9;script:</c> is caught.
/// </summary>
internal static class UrlSanitizer
{
    // Replacement for a blocked URL. about:blank is inert in href/src and won't navigate.
    private const string Neutralized = "about:blank";

    /// <summary>
    ///     Sanitizes a navigation/resource URL (<c>href</c>, <c>cite</c>, <c>action</c>,
    ///     <c>iframe</c>/<c>script</c>/<c>object</c> sources). Blocks <c>javascript:</c>,
    ///     <c>vbscript:</c>, and all <c>data:</c> URLs.
    /// </summary>
    public static string? Sanitize(string? url) => SanitizeCore(url, allowMediaData: false);

    /// <summary>
    ///     Sanitizes a media URL (<c>img</c>/<c>audio</c>/<c>video</c>/<c>source</c> <c>src</c>,
    ///     <c>poster</c>). Blocks <c>javascript:</c>/<c>vbscript:</c> but allows
    ///     <c>data:image/*</c>, <c>data:video/*</c>, and <c>data:audio/*</c>.
    /// </summary>
    public static string? SanitizeMedia(string? url) => SanitizeCore(url, allowMediaData: true);

    private static string? SanitizeCore(string? url, bool allowMediaData)
    {
        if (url is null)
        {
            return null;
        }

        if (url.StartsWith(RaskUrl.TrustedPrefix, StringComparison.Ordinal))
        {
            return url[RaskUrl.TrustedPrefix.Length..];
        }

        // Allocation-free scheme check: the common safe URL (relative, http(s), mailto, #frag)
        // fails the first prefix at its first char and returns the input unchanged — no string
        // is materialized on the render hot path.
        if (MatchesScheme(url, "javascript:") || MatchesScheme(url, "vbscript:"))
        {
            return Neutralized;
        }

        if (MatchesScheme(url, "data:"))
        {
            var safe = allowMediaData &&
                       (MatchesScheme(url, "data:image/") ||
                        MatchesScheme(url, "data:video/") ||
                        MatchesScheme(url, "data:audio/"));
            return safe ? url : Neutralized;
        }

        return url;
    }

    // True when url, after stripping leading C0 controls/space and dropping embedded control
    // chars (tab/newline/CR/NUL/DEL — what the WHATWG URL parser ignores, plus NUL-stuffing),
    // begins case-insensitively with prefix (which must be lowercase). Embedded spaces are NOT
    // dropped — a space inside a scheme invalidates it, so "java script:" is not treated as
    // javascript (matching browser behaviour). Allocates nothing.
    private static bool MatchesScheme(string url, ReadOnlySpan<char> prefix)
    {
        var i = 0;
        while (i < url.Length && url[i] <= ' ')
        {
            i++;
        }

        var p = 0;
        while (i < url.Length && p < prefix.Length)
        {
            var c = url[i++];
            if (c < ' ' || c == (char)0x7F)
            {
                continue; // drop embedded control / NUL / DEL (but keep space)
            }

            if (char.ToLowerInvariant(c) != prefix[p])
            {
                return false;
            }

            p++;
        }

        return p == prefix.Length;
    }
}
