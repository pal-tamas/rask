namespace Rask.Core;

/// <summary>
///     Opt-out marker for the URL-attribute scheme sanitization Rask applies by default to
///     <c>href</c>/<c>src</c>/<c>action</c>/<c>cite</c>/etc. Wrap a value you fully control in
///     <see cref="Trusted" /> to emit it verbatim (it is still HTML-encoded). Use only for URLs
///     that are not attacker-influenced — e.g. a hard-coded <c>javascript:void(0)</c> sentinel.
/// </summary>
public static class RaskUrl
{
    // A control-char sentinel that cannot occur in a real URL and is stripped before output:
    // UrlSanitizer unwraps it; if it ever leaked unstripped it would be HTML-encoded harmlessly.
    internal const string TrustedPrefix = "rask-trusted:";

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

        // Normalize only the leading portion needed to identify the scheme + data media-type.
        // 32 chars comfortably covers "data:image/svg+xml" and every blocked scheme.
        var head = NormalizeHead(url, 32);

        if (head.StartsWith("javascript:", StringComparison.Ordinal) ||
            head.StartsWith("vbscript:", StringComparison.Ordinal))
        {
            return Neutralized;
        }

        if (head.StartsWith("data:", StringComparison.Ordinal))
        {
            var safe = allowMediaData &&
                       (head.StartsWith("data:image/", StringComparison.Ordinal) ||
                        head.StartsWith("data:video/", StringComparison.Ordinal) ||
                        head.StartsWith("data:audio/", StringComparison.Ordinal));
            return safe ? url : Neutralized;
        }

        return url;
    }

    // Strip leading C0 controls + space, drop every embedded control char (the WHATWG parser
    // removes tab/newline; we drop all <0x20 and 0x7F to also defeat NUL-stuffing), lowercase
    // the rest. Returns at most maxLen chars — enough to match a scheme/media-type prefix.
    private static string NormalizeHead(string url, int maxLen)
    {
        Span<char> buf = stackalloc char[maxLen];
        var n = 0;
        var i = 0;

        while (i < url.Length && url[i] <= ' ')
        {
            i++;
        }

        for (; i < url.Length && n < maxLen; i++)
        {
            var c = url[i];
            if (c <= ' ' || c == (char)0x7F) // NUL/control/space|| c == '')
            {
                continue;
            }

            buf[n++] = char.ToLowerInvariant(c);
        }

        return new string(buf[..n]);
    }
}
