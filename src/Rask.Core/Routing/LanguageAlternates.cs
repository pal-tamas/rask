using System.Net;
using System.Text;

namespace Rask.Core.Routing;

/// <summary>
///     What a page in an app with a URL per language (<see cref="Globalization.RaskCultureOptions.UrlPrefix" />) says
///     about its other languages: a <c>canonical</c> to itself and a <c>&lt;link rel="alternate" hreflang&gt;</c> for
///     every language, plus <c>x-default</c> for the root's.
/// </summary>
/// <remarks>
///     Both hosts write them — a prerender into each page it writes, the server into each page it serves — so a
///     crawler reads the same head either way. A page that declares its own canonical keeps it, and one that writes
///     its own <c>hreflang</c> alternates keeps those: the page is the one place that says.
/// </remarks>
internal static class LanguageAlternates
{
    /// <summary>The <c>hreflang</c> a crawler sends a visitor no listed language fits to: the root's.</summary>
    internal const string XDefault = "x-default";

    /// <summary>
    ///     <paramref name="html" /> with the links that tie it to its other languages in its head, or unchanged when it
    ///     has no head.
    /// </summary>
    /// <param name="html">The page, in <paramref name="language" />.</param>
    /// <param name="languages">The app's URL languages.</param>
    /// <param name="language">The language the page is in, as the app spells it.</param>
    /// <param name="urlIn">The page's absolute URL in a language — a crawler takes nothing else.</param>
    public static string AddTo(string html, UrlLanguages languages, string language, Func<string, string> urlIn)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(urlIn);

        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (head < 0)
        {
            return html;
        }

        var links = new StringBuilder();
        if (CanonicalTarget(html) is null)
        {
            AppendLink(links, "canonical", null, urlIn(language));
        }

        if (html.IndexOf("hreflang=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            foreach (var each in languages.All)
            {
                AppendLink(links, "alternate", each, urlIn(each));
            }

            AppendLink(links, "alternate", XDefault, urlIn(languages.Default));
        }

        return links.Length == 0 ? html : html.Insert(head, links.ToString());
    }

    /// <summary>The <c>href</c> of the document's canonical link, or <c>null</c> when it declares none.</summary>
    internal static string? CanonicalTarget(string html)
    {
        var rel = html.IndexOf("rel=\"canonical\"", StringComparison.OrdinalIgnoreCase);
        if (rel < 0)
        {
            return null;
        }

        // The tag's bounds, so an href from a NEIGHBOURING link cannot be read as this one's. `rel` may
        // come before or after `href` — Link writes href first today, and that is the serializer's
        // business, not this reader's.
        var open = html.LastIndexOf('<', rel);
        var close = html.IndexOf('>', rel);
        if (open < 0 || close < 0)
        {
            return null;
        }

        var tag = html[open..close];
        const string Needle = "href=\"";
        var href = tag.IndexOf(Needle, StringComparison.OrdinalIgnoreCase);
        if (href < 0)
        {
            return null;
        }

        href += Needle.Length;
        var end = tag.IndexOf('"', href);
        return end < 0 ? null : tag[href..end];
    }

    private static void AppendLink(StringBuilder links, string rel, string? hreflang, string href)
    {
        links.Append("<link rel=\"").Append(rel).Append('"');
        if (hreflang is not null)
        {
            links.Append(" hreflang=\"").Append(WebUtility.HtmlEncode(hreflang)).Append('"');
        }

        links.Append(" href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">");
    }
}
