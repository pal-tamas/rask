using System.Collections.Frozen;
using System.Globalization;

namespace Rask;

/// <summary>
///     The tags and attributes the editor's own schema writes — Tiptap's starter kit with the extensions the kit
///     bundles (<c>Resources/editor/ui-editor.ts</c>) — and nothing else: what <see cref="UiEditorHtml" /> keeps.
/// </summary>
internal static class UiEditorSchema
{
    private static readonly FrozenSet<string> Tags = new[]
    {
        "p", "br", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "b", "em", "i", "u", "s", "strike", "del", "mark",
        "sub", "sup", "code", "pre", "blockquote", "ul", "ol", "li", "hr", "a", "table", "colgroup", "col", "thead",
        "tbody", "tr", "th", "td",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Voids = new[] { "br", "hr", "col" }.ToFrozenSet(StringComparer.Ordinal);

    // Elements whose content is not text a reader wrote — a script's source, a style sheet, a frame — so it goes
    // with the tag instead of being kept as text.
    private static readonly FrozenSet<string> Opaque = new[]
    {
        "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes", "noscript", "plaintext",
        "template", "object", "svg", "math", "select",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Schemes = new[] { "http", "https", "mailto", "tel" }
        .ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Rels = new[] { "noopener", "noreferrer", "nofollow", "ugc" }
        .ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Alignments = new[] { "left", "center", "right", "justify" }
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether the schema writes <paramref name="tag" /> (lower case).</summary>
    internal static bool Keeps(string tag) => Tags.Contains(tag);

    /// <summary>Whether <paramref name="tag" /> has no end tag and no content.</summary>
    internal static bool IsVoid(string tag) => Voids.Contains(tag);

    /// <summary>Whether what is inside <paramref name="tag" /> is dropped along with it.</summary>
    internal static bool DropsContent(string tag) => Opaque.Contains(tag);

    /// <summary>
    ///     The value <paramref name="name" /> keeps on <paramref name="tag" /> (both lower case), from its decoded
    ///     <paramref name="value" />; <see langword="null" /> when the schema does not write it, or not with that value.
    /// </summary>
    internal static string? Attribute(string tag, string name, string value) => (tag, name) switch
    {
        (_, "style") => Style(value),
        ("a", "href") => Href(value),
        ("a", "target") => string.Equals(value, "_blank", StringComparison.Ordinal) ? value : null,
        ("a", "rel") => Rel(value),
        ("ol", "start") => Whole(value),
        ("ol", "type") => value is "1" or "a" or "A" or "i" or "I" ? value : null,
        ("td" or "th", "colspan" or "rowspan") => Whole(value),
        ("td" or "th", "colwidth") => value.All(c => char.IsAsciiDigit(c) || c == ',') ? value : null,
        ("code", "class") => value.StartsWith("language-", StringComparison.Ordinal)
                             && value.Skip(9).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+')
            ? value
            : null,
        _ => null,
    };

    // A link the reader follows: the web, mail and the phone, or a path of the site's own. Anything else that names
    // a scheme — javascript:, data:, vbscript: — is not a link the editor writes. The control characters and the
    // leading space a browser skips when it reads a URL are skipped here too, so "java&#9;script:" is caught.
    private static string? Href(string value)
    {
        var url = new string(value.Where(c => c >= ' ' && c != (char)0x7F).ToArray()).Trim();
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var path = url.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (path >= 0 && path < colon))
        {
            return url;
        }

        return Schemes.Contains(url[..colon].ToLowerInvariant()) ? url : null;
    }

    private static string? Rel(string value)
    {
        var kept = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(Rels.Contains).ToArray();
        return kept.Length == 0 ? null : string.Join(' ', kept);
    }

    private static string? Whole(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and < 10_000
            ? value
            : null;

    // TextAlign's text-align and a table's widths: the declarations the schema writes, each checked on its own.
    private static string? Style(string value)
    {
        var kept = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Declaration)
            .OfType<string>()
            .ToArray();
        return kept.Length == 0 ? null : string.Join("; ", kept);
    }

    private static string? Declaration(string declaration)
    {
        var colon = declaration.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return null;
        }

        var property = declaration[..colon].Trim().ToLowerInvariant();
        var value = declaration[(colon + 1)..].Trim().ToLowerInvariant();
        var kept = property switch
        {
            "text-align" => Alignments.Contains(value),
            "width" or "min-width" => Pixels(value),
            _ => false,
        };
        return kept ? property + ": " + value : null;
    }

    private static bool Pixels(string value) =>
        value.EndsWith("px", StringComparison.Ordinal)
        && double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size)
        && size < 100_000;
}
