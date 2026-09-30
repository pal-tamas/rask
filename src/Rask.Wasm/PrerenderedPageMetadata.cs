using System.Globalization;
using Rask.Core.Live;

namespace Rask.Wasm;

/// <summary>What a prerendered page's own HTML says about it: its last-modified date, its canonical URL and whether it may be indexed.</summary>
internal static class PrerenderedPageMetadata
{
    // The profiles of the W3C datetime a sitemap's lastmod takes that carry a time: minutes, seconds, or
    // fractional seconds, each with or without a zone (none is read as UTC).
    private static readonly string[] Timestamps =
        ["yyyy-MM-dd'T'HH:mmK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    /// <summary>
    ///     The page's <c>article:modified_time</c>, as a sitemap <c>lastmod</c> — or <c>null</c> when it
    ///     declares none, or declares something that is not a date.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read off the page, for the reason noindex and the canonical are: the page is the one place the
    ///         date is stated, so the sitemap cannot claim a different one. Open Graph's tag rather than a
    ///         Rask-specific one, because a page that wants a <c>lastmod</c> almost always wants the tag
    ///         anyway, and a second declaration of the same fact is the thing that drifts.
    ///     </para>
    ///     <para>
    ///         <b>Never invented.</b> The obvious fallback — the time of the publish — marks every URL as
    ///         changed on every deploy, and a crawler that notices stops reading the field for the whole site.
    ///         A page with no date gets a <c>&lt;url&gt;</c> with no <c>lastmod</c>, which is what the protocol
    ///         says to do.
    ///     </para>
    ///     <para>
    ///         Normalised to a W3C datetime, because a <c>&lt;meta&gt;</c> will carry anything and one malformed
    ///         <c>lastmod</c> is reported against the whole sitemap rather than against its URL.
    ///     </para>
    /// </remarks>
    internal static string? LastModifiedOf(string html)
    {
        var property = html.IndexOf("property=\"article:modified_time\"", StringComparison.OrdinalIgnoreCase);
        if (property < 0)
        {
            return null;
        }

        // The tag's own bounds, so a content attribute from a NEIGHBOURING meta cannot be read as this one's.
        var open = html.LastIndexOf('<', property);
        var close = html.IndexOf('>', property);
        if (open < 0 || close < 0)
        {
            return null;
        }

        var tag = html[open..close];
        const string Needle = "content=\"";
        var content = tag.IndexOf(Needle, StringComparison.OrdinalIgnoreCase);
        if (content < 0)
        {
            return null;
        }

        content += Needle.Length;
        var end = tag.IndexOf('"', content);
        if (end < 0)
        {
            return null;
        }

        // Decoded: the serializer writes an offset's '+' as an entity, and "&#x2B;01:00" is not a date.
        return W3cDateTime(System.Net.WebUtility.HtmlDecode(tag[content..end]).Trim());
    }

    /// <summary>
    ///     <paramref name="value" /> as a W3C datetime — <c>YYYY</c>, <c>YYYY-MM</c>, <c>YYYY-MM-DD</c> or a
    ///     timestamp — or <c>null</c> when it is none of them.
    /// </summary>
    /// <remarks>
    ///     Parsed EXACTLY. A culture-aware parse reads "01/02/2026" and "Sep 10, 2026" as dates and publishes
    ///     its guess about which day was meant; the whole point of the field is that it is not a guess.
    /// </remarks>
    private static string? W3cDateTime(string value)
    {
        if (value.Length == 4 && value.All(char.IsAsciiDigit))
        {
            return value;
        }

        if (value.Length == 7
            && value[4] == '-'
            && DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return value;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return value;
        }

        return DateTimeOffset.TryParseExact(
            value, Timestamps, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            ? moment.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Whether a written page belongs in the sitemap.</summary>
    internal static bool ListedInSitemap(string html, string routePath) =>
        !IsNoIndex(html) && (CanonicalTarget(html) is not { } canonical || SamePage(canonical, routePath));

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

    /// <summary>Whether a canonical URL names the route it was rendered for.</summary>
    /// <remarks>
    ///     Compared on the PATH, because the canonical is absolute and the route is not, and the origin
    ///     is the app's to choose. A trailing slash is not a difference: a static host serves
    ///     <c>/docs/</c> and <c>/docs</c> as the same document, and a page that spells its canonical the
    ///     other way has not said anything about a different page.
    /// </remarks>
    private static bool SamePage(string canonical, string routePath)
    {
        var path = Uri.TryCreate(canonical, UriKind.Absolute, out var uri) ? uri.AbsolutePath : canonical;
        var expected = LiveOptions.PathBase + routePath;

        return string.Equals(path.TrimEnd('/'), expected.TrimEnd('/'), StringComparison.Ordinal);
    }

    /// <summary>Whether the rendered document asks robots not to index it.</summary>
    /// <remarks>
    ///     A deliberately narrow reader: the <c>content</c> of a <c>&lt;meta name="robots"&gt;</c>, looked
    ///     at for the word <c>noindex</c>. It is checked against the DOCUMENT rather than against a
    ///     separate declaration because a page that says one thing in its head and another in a build
    ///     configuration is the failure this avoids, not one it should be able to express.
    /// </remarks>
    internal static bool IsNoIndex(string html)
    {
        var robots = html.IndexOf("name=\"robots\"", StringComparison.OrdinalIgnoreCase);
        if (robots < 0)
        {
            return false;
        }

        var tagEnd = html.IndexOf('>', robots);
        var tag = tagEnd < 0 ? html[robots..] : html[robots..tagEnd];
        return tag.Contains("noindex", StringComparison.OrdinalIgnoreCase);
    }
}
