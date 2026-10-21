namespace Rask.Storage.Upload;

/// <summary>
/// What counts as a media type here: <c>type/subtype</c>, each an RFC 9110 token, and nothing more — no parameters, no
/// whitespace, no line break. A stated type reaches a response header, so anything looser is refused, not cleaned.
/// </summary>
internal static class MediaType
{
    /// <summary>A <c>type/subtype</c>, or with <paramref name="family"/> also a <c>type/*</c>.</summary>
    internal static bool IsValid(string? value, bool family = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || value.IndexOf('/', slash + 1) >= 0)
        {
            return false;
        }

        var subtype = value.AsSpan(slash + 1);
        return IsToken(value.AsSpan(0, slash)) && ((family && subtype is "*") || IsToken(subtype));
    }

    /// <summary>The type a caller stated with <c>.As(…)</c>, lower-cased — or a refusal naming what was wrong.</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is not a plain <c>type/subtype</c>.</exception>
    internal static string Stated(string contentType, string parameterName)
    {
        if (contentType is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (!IsValid(contentType))
        {
            throw new ArgumentException(
                "A stated content type is a plain media type, like .As(\"application/xml\"): a type and a subtype, with no "
                + "parameters, spaces or line breaks.",
                parameterName);
        }

        return contentType.ToLowerInvariant();
    }

    private static bool IsToken(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '&' or '^' or '_' or '.' or '+' or '-'))
            {
                return false;
            }
        }

        return !value.IsEmpty;
    }
}
