using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;

namespace Rask;

/// <summary>
///     The markup <see cref="UiEditor" /> keeps of its value: the tags and attributes Tiptap's starter kit and the
///     extensions the kit bundles write, and those an app's own extension registers with <see cref="Allow(string, string[])" />.
/// </summary>
/// <remarks>
///     <para>
///     The value is cut to this on the page's first paint and on every change the browser sends, so a stored value
///     cannot run script. An extension added on the <c>ui:editor</c> event writes markup of its own — an image, a
///     mention — and registers it once, at startup:
///     </para>
///     <code>
///     UiEditorSchema.Allow("img", "src", "alt", "width", "height");
///     UiEditorSchema.Allow("span", "data-mention", id =&gt; Guid.TryParse(id, out _));
///     </code>
///     <para>
///     What a registration cannot do is let script back in: a tag that runs or embeds something (<c>script</c>,
///     <c>style</c>, <c>iframe</c>, <c>object</c>, <c>svg</c>, a form control…), an event handler (<c>on…</c>),
///     <c>style</c>, <c>srcdoc</c> or <c>srcset</c> are refused, and a URL attribute (<c>href</c>, <c>src</c>, …) is
///     still held to http, https, mailto, tel or a path — <c>src</c> also to an inline PNG, GIF, JPEG or WebP image.
///     An attribute the kit's own schema already rules (<c>a href</c>, <c>style</c>, …) keeps the kit's rule.
///     </para>
/// </remarks>
public static class UiEditorSchema
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

    // Tags a registration may not add: what runs, embeds, frames or submits something, or reshapes the document.
    private static readonly FrozenSet<string> Refused = new[]
    {
        "base", "link", "meta", "html", "head", "body", "form", "input", "button", "frame", "frameset", "embed",
        "applet", "portal", "fencedframe", "image",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> RefusedAttributes = new[] { "style", "srcdoc", "srcset", "is", "xmlns", "formaction" }
        .ToFrozenSet(StringComparer.Ordinal);

    // Attributes a browser follows or fetches: whatever a registration says of the value, it is still a link.
    private static readonly FrozenSet<string> Urls = new[]
    {
        "href", "src", "cite", "action", "poster", "background", "longdesc", "ping", "data", "xlink:href", "manifest",
    }.ToFrozenSet(StringComparer.Ordinal);

    // What the app's extensions registered: tag → attribute → the rule its value is held to (null: any value).
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Func<string, bool>?>> Registered =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Keeps <paramref name="tag" />, with <paramref name="attributes" /> at any value, in every editor's value: the
    ///     markup an app's own Tiptap extension writes. Call it once, at startup; calling it again adds to it.
    /// </summary>
    /// <param name="tag">The element's name: <c>"img"</c>, <c>"span"</c>. A tag the kit already keeps takes the attributes.</param>
    /// <param name="attributes">The attributes kept on it. A URL attribute is still held to a safe link.</param>
    /// <exception cref="ArgumentException">
    ///     A tag that runs or embeds something, or an event handler, <c>style</c>, <c>srcdoc</c> or <c>srcset</c>.
    /// </exception>
    public static void Allow(string tag, params string[] attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var rules = Rules(tag);
        foreach (var attribute in attributes)
        {
            rules[AttributeName(attribute)] = null;
        }
    }

    /// <summary>
    ///     Keeps <paramref name="tag" /> with <paramref name="attribute" />, at a value <paramref name="accepts" /> says
    ///     yes to: <c>UiEditorSchema.Allow("span", "data-mention", id =&gt; Guid.TryParse(id, out _))</c>.
    /// </summary>
    /// <param name="tag">The element's name.</param>
    /// <param name="attribute">The attribute kept on it.</param>
    /// <param name="accepts">Whether a value (decoded) is kept; one it refuses drops the attribute, not the element.</param>
    /// <exception cref="ArgumentException">As <see cref="Allow(string, string[])" />.</exception>
    public static void Allow(string tag, string attribute, Func<string, bool> accepts)
    {
        ArgumentNullException.ThrowIfNull(accepts);
        Rules(tag)[AttributeName(attribute)] = accepts;
    }

    private static ConcurrentDictionary<string, Func<string, bool>?> Rules(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var name = tag.ToLowerInvariant();
        if (!IsName(name) || Opaque.Contains(name) || Refused.Contains(name))
        {
            throw new ArgumentException(
                $"'{tag}' cannot be kept in an editor's value: it is not an element name, or it runs, embeds or submits something.",
                nameof(tag));
        }

        return Registered.GetOrAdd(name, static _ => new ConcurrentDictionary<string, Func<string, bool>?>(StringComparer.Ordinal));
    }

    private static string AttributeName(string attribute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        var name = attribute.ToLowerInvariant();
        if (!IsName(name) || name.StartsWith("on", StringComparison.Ordinal) || RefusedAttributes.Contains(name))
        {
            throw new ArgumentException(
                $"'{attribute}' cannot be kept in an editor's value: it is not an attribute name, or it is an event handler, "
                + "a style or a document of its own.",
                nameof(attribute));
        }

        return name;
    }

    private static bool IsName(string name) =>
        name.Length > 0 && char.IsAsciiLetterLower(name[0]) && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or ':');

    /// <summary>Whether the schema writes <paramref name="tag" /> (lower case).</summary>
    internal static bool Keeps(string tag) => Tags.Contains(tag) || Registered.ContainsKey(tag);

    /// <summary>Whether <paramref name="tag" /> has no end tag and no content.</summary>
    internal static bool IsVoid(string tag) => Voids.Contains(tag);

    /// <summary>Whether what is inside <paramref name="tag" /> is dropped along with it.</summary>
    internal static bool DropsContent(string tag) => Opaque.Contains(tag);

    /// <summary>
    ///     The value <paramref name="name" /> keeps on <paramref name="tag" /> (both lower case), from its decoded
    ///     <paramref name="value" />; <see langword="null" /> when the schema does not write it, or not with that value.
    /// </summary>
    internal static string? Attribute(string tag, string name, string value)
    {
        if (Registration(tag, name) is not { } rule)
        {
            return Kit(tag, name, value);
        }

        if (rule.Accepts is { } accepts && !accepts(value))
        {
            return null;
        }

        return Urls.Contains(name) ? Url(name, value) : value;
    }

    // The registered rule for an attribute the kit's own schema does not rule; null when there is none.
    private static (bool Registered, Func<string, bool>? Accepts)? Registration(string tag, string name) =>
        !RuledByKit(tag, name) && Registered.TryGetValue(tag, out var rules) && rules.TryGetValue(name, out var accepts)
            ? (true, accepts)
            : null;

    private static bool RuledByKit(string tag, string name) => (tag, name) is (_, "style") or ("a", "href" or "target" or "rel")
        or ("ol", "start" or "type") or ("td" or "th", "colspan" or "rowspan" or "colwidth") or ("code", "class");

    private static string? Url(string name, string value) =>
        string.Equals(name, "src", StringComparison.Ordinal) && IsInlineImage(value) ? value : Href(value);

    // An image written into the value itself, of a raster type: SVG can carry script, so it is not one.
    private static bool IsInlineImage(string value) =>
        value.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
        && value.TrimStart()[11..] is var rest
        && (rest.StartsWith("png;", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("gif;", StringComparison.OrdinalIgnoreCase)
            || rest.StartsWith("jpeg;", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("webp;", StringComparison.OrdinalIgnoreCase));

    private static string? Kit(string tag, string name, string value) => (tag, name) switch
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
