namespace Rask.Storage;

/// <summary>
/// How a file's object key is derived from its id: <c>{prefix}{first two hex digits}/{id as 32 hex digits}</c>.
/// </summary>
/// <remarks>
/// <para>
/// From the id alone, deliberately. That is what lets <see cref="IFiles.Url"/> build a public URL with no
/// database read, which is what makes it usable inside a render. The two-digit fan-out keeps any one
/// directory of the disk store to a few thousand entries per million files.
/// </para>
/// <para>
/// Keys are generated here and nowhere else — an uploaded file name never reaches a path or a URL — and
/// <see cref="TryParse"/> is how the sweep tells this app's objects from anything else under the prefix,
/// which it never deletes.
/// </para>
/// </remarks>
internal static class KeyLayout
{
    private const int IdLength = 32;

    internal static string KeyOf(string prefix, Guid id)
    {
        var hex = id.ToString("N");
        return string.Concat(prefix, hex.AsSpan(0, 2), "/", hex);
    }

    internal static bool TryParse(string key, string prefix, out Guid id)
    {
        id = default;
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = key.AsSpan(prefix.Length);
        if (rest.Length != 2 + 1 + IdLength || rest[2] != '/')
        {
            return false;
        }

        var hex = rest[3..];
        foreach (var c in hex)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return hex[..2].SequenceEqual(rest[..2]) && Guid.TryParseExact(hex, "N", out id);
    }

    /// <summary>
    /// Lowercase letters, digits, <c>. _ - /</c>; no empty, <c>.</c> or <c>..</c> segment; no leading slash.
    /// Checked by every backend before a key becomes a path or a URL, because both collapse <c>..</c>.
    /// </summary>
    internal static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 512)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-' or '/'))
            {
                return false;
            }
        }

        foreach (var segment in key.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidPrefix(string prefix) =>
        prefix.Length == 0 || (prefix.EndsWith('/') && IsValid(prefix[..^1]));
}
