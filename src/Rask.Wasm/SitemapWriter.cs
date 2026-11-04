using System.Text;
using Rask.Core.Live;

using static Rask.Wasm.WasmPrerender;

namespace Rask.Wasm;

/// <summary>Writes <c>sitemap.xml</c> and <c>robots.txt</c> for the prerendered site, with each page's own last-modified date.</summary>
internal static class SitemapWriter
{
    /// <summary>
    ///     Writes <c>sitemap.xml</c> for the pages that reached disk, and a <c>robots.txt</c> pointing at
    ///     it, when the app has named the origin it will be served from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built from what was WRITTEN rather than from the route table, because those are different
    ///         lists and the difference is the whole point: a route the pass skipped still answers, but
    ///         with the boot shell, and a sitemap is a promise that the URL has content.
    ///     </para>
    ///     <para>
    ///         An existing <c>robots.txt</c> is never overwritten. It is a file with real consequences —
    ///         a wrong one delists a site — so an author who shipped one has said something this pass has
    ///         no business editing, and it says how to add the sitemap line instead.
    ///     </para>
    /// </remarks>
    internal static void WriteSitemap(
        string outputDirectory, List<SitemapEntry> paths, List<string> writtenFiles, PrerenderedLanguages? languages = null)
    {
        var origin = SiteOrigin();
        if (string.IsNullOrEmpty(origin))
        {
            // Said out loud. A missing sitemap is invisible in a browser and would be invisible in the
            // build output too, and "we shipped for months without one" is how that ends. Not a warning:
            // an app with no fixed origin is a legitimate configuration, and guessing a domain into a
            // published file is worse than shipping no sitemap.
            Console.WriteLine(
                "[Rask.Prerender] no sitemap — set <RaskSiteUrl>https://example.com</RaskSiteUrl> to "
                + "publish one (a sitemap carries absolute URLs, so the origin cannot be inferred)");
            return;
        }

        if (paths.Count == 0)
        {
            Console.WriteLine("[Rask.Prerender] no sitemap — no page was written");
            return;
        }

        var sitemap = Path.Combine(outputDirectory, SitemapFileName);
        File.WriteAllText(sitemap, SitemapXml(origin, paths, languages));
        writtenFiles.Add(sitemap);
        Console.WriteLine($"[Rask.Prerender] wrote {SitemapFileName} with {paths.Count} URL(s)");

        WriteRobots(outputDirectory, $"{origin}{LiveOptions.PathBase}/{SitemapFileName}", writtenFiles);
    }

    private static string SitemapXml(string origin, List<SitemapEntry> paths, PrerenderedLanguages? languages)
    {
        // A page's other languages, by the route they are all of — only the ones that reached the sitemap, since
        // an alternate is a promise of the same page in another language, and a skipped one has none.
        var siblings = paths
            .Where(entry => entry is { Page: not null, Language: not null })
            .GroupBy(entry => entry.Page!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.DistinctBy(entry => entry.Language, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"");
        builder.AppendLine(siblings.Count > 0 ? " xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">" : ">");
        var trailingSlash = HostServesTrailingSlash();

        foreach (var entry in paths)
        {
            // LiveOptions.PathBase is already on every rendered link; it belongs here too, or a
            // sub-path deploy publishes a sitemap pointing at the origin root.
            var url = origin + LiveOptions.PathBase + SiteUrlPath(entry.Path, trailingSlash);
            builder.Append("  <url><loc>").Append(XmlEscape(url)).Append("</loc>");
            if (entry.LastModified is { } lastModified)
            {
                builder.Append("<lastmod>").Append(XmlEscape(lastModified)).Append("</lastmod>");
            }

            if (entry.Page is { } page && languages is not null && siblings.TryGetValue(page, out var others))
            {
                AppendAlternates(builder, origin, others, languages, trailingSlash);
            }

            builder.AppendLine("</url>");
        }

        builder.AppendLine("</urlset>");
        return builder.ToString();
    }

    // Every language the page was listed in, the entry's own included — the protocol asks each <url> to name all
    // of them — and x-default for the root's.
    private static void AppendAlternates(
        StringBuilder builder, string origin, List<SitemapEntry> siblings, PrerenderedLanguages languages, bool trailingSlash)
    {
        foreach (var sibling in siblings)
        {
            AppendAlternate(builder, sibling.Language!, origin + LiveOptions.PathBase + SiteUrlPath(sibling.Path, trailingSlash));
        }

        if (languages.Languages is { } urlLanguages
            && siblings.Exists(sibling => string.Equals(sibling.Language, urlLanguages.Default, StringComparison.Ordinal)))
        {
            AppendAlternate(
                builder,
                PrerenderedLanguages.XDefault,
                languages.Url(origin, urlLanguages.Default, siblings[0].Page!, trailingSlash));
        }
    }

    private static void AppendAlternate(StringBuilder builder, string hreflang, string href) =>
        builder.Append("<xhtml:link rel=\"alternate\" hreflang=\"").Append(XmlEscape(hreflang))
            .Append("\" href=\"").Append(XmlEscape(href)).Append("\"/>");

    /// <summary>The origin the app says it is served from, without a trailing slash, or <c>null</c>.</summary>
    internal static string? SiteOrigin() =>
        Environment.GetEnvironmentVariable(SiteUrlVariable)?.Trim().TrimEnd('/') is { Length: > 0 } origin ? origin : null;

    internal static void WriteRobots(string outputDirectory, string sitemapUrl, List<string> writtenFiles)
    {
        var robots = Path.Combine(outputDirectory, RobotsFileName);

        // "Existing" is not the same question as "the app's own" — the second publish into a directory
        // finds the FIRST publish's robots.txt sitting there, and calling that one the app's leaves it
        // frozen at whatever origin the earlier run was given. Same family of bug as the shell (#1036),
        // with a quieter symptom: an app that changes RaskSiteUrl keeps publishing a robots.txt
        // pointing a crawler at the old domain's sitemap, and the log says the author asked for it.
        if (File.Exists(robots) && !IsOwnRobots(File.ReadAllText(robots)))
        {
            Console.WriteLine(
                $"[Rask.Prerender] kept the app's own {RobotsFileName} — add "
                + $"\"Sitemap: {sitemapUrl}\" to it yourself");
            return;
        }

        File.WriteAllText(robots, RobotsFor(sitemapUrl));
        writtenFiles.Add(robots);
        Console.WriteLine($"[Rask.Prerender] wrote {RobotsFileName}");
    }

    /// <summary>The whole of the <c>robots.txt</c> this pass writes, for a given sitemap URL.</summary>
    private static string RobotsFor(string sitemapUrl) =>
        $"User-agent: *\nAllow: /\nSitemap: {sitemapUrl}\n";

    /// <summary>
    ///     Whether a <c>robots.txt</c> is one an earlier run of this pass wrote, rather than the app's.
    /// </summary>
    /// <remarks>
    ///     Matched on the exact three lines it writes, for ANY sitemap URL — so a file an author edited
    ///     even slightly is the author's, and stays untouched. The bar is deliberately that high:
    ///     getting this wrong overwrites a file that can delist a site.
    /// </remarks>
    private static bool IsOwnRobots(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        return lines.Length == 4
               && string.Equals(lines[0], "User-agent: *", StringComparison.Ordinal)
               && string.Equals(lines[1], "Allow: /", StringComparison.Ordinal)
               && lines[2].StartsWith("Sitemap: ", StringComparison.Ordinal)
               && lines[3].Length == 0;
    }

    /// <summary>
    ///     A route path in the form the host serves without redirecting.
    /// </summary>
    /// <remarks>
    ///     The root is always <c>/</c> — it is already a directory URL, and doubling the slash would
    ///     name a different resource.
    /// </remarks>
    internal static string SiteUrlPath(string path, bool trailingSlash)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/";
        }

        return trailingSlash ? trimmed + "/" : trimmed;
    }

    /// <summary>Off only when the app says its host strips the slash; see <see cref="TrailingSlashVariable" />.</summary>
    internal static bool HostServesTrailingSlash() =>
        !string.Equals(
            Environment.GetEnvironmentVariable(TrailingSlashVariable)?.Trim(),
            "false",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Escapes the five XML entities. A route template cannot contain them today; a route is
    /// author-written text, and a sitemap that silently stops parsing is not worth the assumption.</summary>
    private static string XmlEscape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    /// <summary>
    ///     One URL the sitemap lists, and the date its page says it last changed — and, for an app with languages in
    ///     its URLs, which language it is in and which route it is of, so its siblings can be named.
    /// </summary>
    internal readonly record struct SitemapEntry(string Path, string? LastModified)
    {
        /// <summary>The page's language, or <c>null</c> when the app keeps languages out of its URLs.</summary>
        public string? Language { get; init; }

        /// <summary>The route the page is of, the same in every language: <c>/about</c> for <c>/en/about</c>.</summary>
        public string? Page { get; init; }
    }
}
