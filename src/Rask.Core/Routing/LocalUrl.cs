namespace Rask.Core.Routing;

/// <summary>
///     Open-redirect guard shared by every Rask redirect path (server post-sign-in
///     <c>returnUrl</c>, the client route-guard challenge, and the WASM login/logout flows). It
///     mirrors ASP.NET's <c>Url.IsLocalUrl</c>: only a same-origin absolute path may pass through;
///     anything that could navigate off the origin collapses to <c>"/"</c>.
/// </summary>
public static class LocalUrl
{
    /// <summary>
    ///     Returns <paramref name="url" /> unchanged when it is a guaranteed-local absolute path,
    ///     otherwise <c>"/"</c>. Rejects: null/empty; non-rooted values (<c>"evil.com"</c>,
    ///     <c>"https://evil.com"</c>, <c>"javascript:…"</c>); protocol-relative URLs
    ///     (<c>"//evil.com"</c>); the backslash variants browsers normalise into them
    ///     (<c>"/\evil.com"</c>, <c>"\evil.com"</c>); and any value containing a control character
    ///     (which can smuggle the value past downstream parsers / split headers).
    /// </summary>
    public static string Sanitize(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "/";
        }

        if (url[0] != '/'
            || (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
            || ContainsControlCharacter(url))
        {
            return "/";
        }

        return url;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
