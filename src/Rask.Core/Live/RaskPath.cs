namespace Rask.Core.Live;

/// <summary>
///     Helpers for the <see cref="RaskLiveOptions.PathBase" /> string. Normalize
///     to <c>""</c> (root, default) or <c>"/segment"</c> (leading slash, no
///     trailing slash). Multi-segment values like <c>"/a/b"</c> are preserved.
/// </summary>
public static class RaskPath
{
    /// <summary>
    ///     Returns <c>""</c> if the input is null/empty/"/"/whitespace; otherwise
    ///     returns the input with leading slash ensured and trailing slash stripped.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim();
        if (string.Equals(s, "/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (s[0] != '/')
        {
            s = "/" + s;
        }

        while (s.Length > 1 && s[^1] == '/')
        {
            s = s[..^1];
        }

        return s;
    }
}
