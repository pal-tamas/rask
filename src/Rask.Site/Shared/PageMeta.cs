using System.Globalization;
using Rask.Core.Live;

namespace Rask.Site;

/// <summary>
///     What a guide adds to the head beyond what every page carries.
/// </summary>
/// <param name="Section">
///     The catalog group the guide sits in — <c>article:section</c>, and the article's
///     <c>articleSection</c> in the structured data.
/// </param>
/// <param name="Modified">
///     When the guide's source last changed, as git recorded it; <c>null</c> when the build could not say,
///     in which case no date is claimed anywhere.
/// </param>
/// <param name="MarkdownUrl">The absolute URL of the guide's Markdown twin, advertised as an alternate.</param>
public sealed record PageArticle(string Section, DateOnly? Modified, string MarkdownUrl);

/// <summary>
///     The head metadata every page owes a crawler: a title of its own, a description of its own, a
///     canonical URL saying which address is the real one, and the structured data saying what the page is.
/// </summary>
/// <remarks>
///     <para>
///         A helper rather than twenty hand-written blocks, because the failure mode is silence. A page
///         that forgets its description inherits the site-wide one from <c>App</c>, which renders, passes
///         every validator, and tells a search engine that twenty URLs are the same page. A page that
///         forgets its canonical is worse: the showcase is reachable at a path and at that path with a
///         trailing slash, and a crawler that cannot tell which is authoritative splits the ranking
///         between them.
///     </para>
///     <para>
///         <b>The framework resolves these as singletons, so a page's values REPLACE the App's</b> —
///         <c>title</c>, <c>meta[name]</c>, <c>meta[property]</c> and <c>link[rel=canonical]</c> are keyed
///         by what they name, and the last contributor wins. Before that, a page declaring its own
///         description got two of them.
///     </para>
///     <para>
///         No <c>og:image</c>. A social card wants a 1200×630 raster, and shipping a machine-drawn one as
///         the site's face is worse than shipping none — a consumer that finds no image falls back to the
///         page's title and description, which are real. <c>twitter:card</c> is <c>summary</c> for the
///         same reason: <c>summary_large_image</c> without an image renders as a blank panel.
///     </para>
/// </remarks>
/// <remarks>
///     <c>[RaskMarkup]</c> because a <c>static class</c> can derive from nothing, so it cannot take the
///     usual <c>: RaskMarkup</c> base — and without one of the two, <c>Title</c>, <c>Meta</c> and
///     <c>Link</c> are simply not in scope here, since the chain entries are injected into markup hosts
///     rather than being ordinary types you can name.
/// </remarks>
[RaskMarkup]
public static partial class PageMeta
{
    /// <summary>
    ///     The origin every canonical and Open Graph URL is built from.
    /// </summary>
    /// <remarks>
    ///     Absolute, because both are defined to be — a relative canonical is ignored, and a relative
    ///     <c>og:url</c> is dropped by every consumer. It matches <c>&lt;RaskSiteUrl&gt;</c> in the
    ///     csproj, which is what the prerender pass builds <c>sitemap.xml</c> from; the two are asserted
    ///     to agree by <c>PageMetaTests</c>, since a site whose sitemap and canonicals disagree about its
    ///     own address is telling a crawler two different things.
    /// </remarks>
    public const string Origin = "https://rask.sh";

    /// <summary>What every page title ends with, and what a page's name is its title without.</summary>
    public const string TitleSuffix = " — Rask";

    /// <summary>
    ///     A route path in the form GitHub Pages serves without redirecting: with a trailing slash.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The prerender pass writes <c>{route}/index.html</c>, and GitHub Pages answers
    ///         <c>/docs/pwa</c> with a <b>301 to <c>/docs/pwa/</c></b> — verified against the live site,
    ///         not assumed. Naming the bare form in a canonical therefore has the page served at
    ///         <c>/docs/pwa/</c> declaring that the real URL is one that redirects straight back to it.
    ///         That is a contradiction rather than a hop, and Search Console reports it as "page with
    ///         redirect" across every URL on the site.
    ///     </para>
    ///     <para>
    ///         In-app links stay bare: the router never issues a request for them, so the slash would be
    ///         noise in the address bar. Only the URLs a crawler resolves — canonical, <c>og:url</c> and
    ///         the sitemap — have to name what the host actually serves.
    ///     </para>
    ///     <para>
    ///         Held in step with <c>&lt;RaskSiteTrailingSlash&gt;</c>, which is what the pass builds
    ///         <c>sitemap.xml</c> from, by <c>PageMetaTests</c>. A site whose sitemap and canonicals
    ///         disagree about the shape of its own URLs is telling a crawler two different things.
    ///     </para>
    /// </remarks>
    public static string CanonicalPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed + "/";
    }

    /// <summary>
    ///     The head block for one page: its title, its description, its canonical, the Open Graph and
    ///     Twitter tags built from the same three values, and its JSON-LD graph.
    /// </summary>
    /// <param name="title">
    ///     The page's own title. Rendered as-is, so it should end with <see cref="TitleSuffix" /> — "Todos —
    ///     Rask" rather than "Todos". Everything before the suffix is the page's name in its breadcrumb.
    /// </param>
    /// <param name="description">
    ///     One or two sentences describing THIS page. Search engines truncate around 160 characters, so
    ///     the first sentence has to stand on its own; <c>PageMetaTests</c> holds every page to it.
    /// </param>
    /// <param name="path">
    ///     The page's route, as a rooted path. Pass a generated <c>Routes.X()</c> rather than a literal:
    ///     a canonical pointing at a URL that has no route behind it is the one SEO mistake that costs
    ///     more than having no canonical at all, and a literal is exactly how the sidebar's fourteen
    ///     entries became dead links when the showcase moved under /docs.
    /// </param>
    /// <param name="article">
    ///     The guide facts, for a page that is an article: it becomes <c>og:type=article</c> with a section
    ///     and a date, a <c>TechArticle</c> in the graph, and it advertises its Markdown twin.
    /// </param>
    public static Component For(string title, string description, string path, PageArticle? article = null)
    {
        var canonicalPath = CanonicalPath(path);
        var url = Origin + LiveOptions.PathBase + canonicalPath;
        var name = title.EndsWith(TitleSuffix, StringComparison.Ordinal) ? title[..^TitleSuffix.Length] : title;

        var head = new List<Component>
        {
            Title[title],
            Meta.Name("description").Content(description),
            Link.Rel("canonical").Href(url),
            Meta.Property("og:type").Content(article is null ? "website" : "article"),
            Meta.Property("og:site_name").Content(SiteIdentity.Name),
            Meta.Property("og:locale").Content("en_US"),
            Meta.Property("og:title").Content(title),
            Meta.Property("og:description").Content(description),
            Meta.Property("og:url").Content(url),
            Meta.Name("twitter:card").Content("summary"),
            Meta.Name("twitter:title").Content(title),
            Meta.Name("twitter:description").Content(description),
        };

        if (article is not null)
        {
            head.Add(Meta.Property("article:section").Content(article.Section));

            // The tag the prerender pass reads the sitemap's <lastmod> back off, so the page, its structured
            // data and the sitemap state one date. Absent rather than guessed when git could not say.
            if (article.Modified is { } modified)
            {
                head.Add(Meta.Property("article:modified_time")
                    .Content(modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            // Where an assistant finds the same guide without the page around it. An alternate, not a
            // canonical: the HTML page stays the one search results name.
            head.Add(Link.Rel("alternate").Type("text/markdown").Href(article.MarkdownUrl));
        }

        var graph = StructuredData.Graph(new StructuredData.Page(
            name, description, url, canonicalPath == "/", Breadcrumb(name, canonicalPath), article));

        // Raw, because a script's content is not HTML: encoding it would turn every quote into an entity
        // and the JSON into something no parser reads. The writer already escaped '<', so it cannot close
        // the element early.
        head.Add(Script.Type("application/ld+json")[Raw.Value(graph)]);

        return [.. head];
    }

    /// <summary>
    ///     The trail from the site root to a page: the site, the docs if the page is under them, the page.
    /// </summary>
    /// <remarks>
    ///     Built from the canonical path, so it can only name URLs that are themselves canonical. The
    ///     front door has none — a one-step breadcrumb is a result that says nothing — and the docs index
    ///     is its own last step rather than a step to itself.
    /// </remarks>
    internal static IReadOnlyList<StructuredData.Crumb> Breadcrumb(string name, string canonicalPath)
    {
        if (canonicalPath == "/")
        {
            return [];
        }

        var root = Origin + LiveOptions.PathBase;
        var docs = CanonicalPath(Features.Routes.GuidesIndexPage());
        var crumbs = new List<StructuredData.Crumb> { new(SiteIdentity.Name, root + "/") };

        if (canonicalPath.StartsWith(docs, StringComparison.Ordinal))
        {
            crumbs.Add(new StructuredData.Crumb("Docs", root + docs));
            if (canonicalPath == docs)
            {
                return crumbs;
            }
        }

        crumbs.Add(new StructuredData.Crumb(name, root + canonicalPath));
        return crumbs;
    }
}
