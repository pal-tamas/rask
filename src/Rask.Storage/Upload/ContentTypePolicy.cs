namespace Rask.Storage.Upload;

/// <summary>
/// What a sniffed media type is allowed to do: be narrowed by a file extension, render inline, pass
/// <see cref="StorageOptions.AllowedTypes"/>.
/// </summary>
internal static class ContentTypePolicy
{
    /// <summary>
    /// The only types ever served <c>inline</c>. Raster images, audio and video: a browser shows them in an
    /// element and none of them can run script. Everything else is an attachment.
    /// </summary>
    private static readonly HashSet<string> Inline = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/avif",
        "audio/mpeg", "audio/aac", "audio/ogg", "audio/wav", "audio/flac", "audio/mp4",
        "video/mp4", "video/quicktime", "video/webm",
    };

    /// <summary>
    /// Types a browser can execute script from. Even as an attachment they are sent as
    /// <c>application/octet-stream</c>, so a mis-configured proxy or an old browser has nothing to render.
    /// </summary>
    private static readonly HashSet<string> ScriptCapable = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml", "image/svg+xml", "application/xml", "text/xml",
    };

    /// <summary>
    /// A name may only NARROW a family the bytes already established, on a fixed list — never promote a file
    /// to an inline type or to markup. <c>evil.png</c> containing HTML stays <c>text/html</c>.
    /// </summary>
    private static readonly Dictionary<string, (string Family, string Type)> Narrowing = new(StringComparer.OrdinalIgnoreCase)
    {
        [".csv"] = ("text/plain", "text/csv"),
        [".md"] = ("text/plain", "text/markdown"),
        [".markdown"] = ("text/plain", "text/markdown"),
        [".json"] = ("text/plain", "application/json"),
        [".docx"] = ("application/zip", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        [".xlsx"] = ("application/zip", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        [".pptx"] = ("application/zip", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
    };

    internal static string Refine(string sniffed, string fileName) =>
        Narrowing.TryGetValue(Path.GetExtension(fileName), out var rule)
        && string.Equals(rule.Family, sniffed, StringComparison.OrdinalIgnoreCase)
            ? rule.Type
            : sniffed;

    internal static bool IsInline(string contentType) => Inline.Contains(contentType);

    internal static string ServedType(string contentType) =>
        ScriptCapable.Contains(contentType) ? ContentSniffer.OctetStream : contentType;

    internal static bool IsAllowed(string contentType, IList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return true;
        }

        var slash = contentType.IndexOf('/', StringComparison.Ordinal);
        foreach (var pattern in allowed)
        {
            if (string.Equals(pattern, contentType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A family never admits a type that can run script: "image/*" is for photographs, and SVG must be named.
            if (slash > 0
                && !ScriptCapable.Contains(contentType)
                && pattern.EndsWith("/*", StringComparison.Ordinal)
                && pattern.Length == slash + 2
                && string.Compare(pattern, 0, contentType, 0, slash, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
