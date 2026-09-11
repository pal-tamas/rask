using System.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;

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
///     canonical URL saying which address is the real one, the card a shared link unfurls with, and the
///     structured data saying what the page is.
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
///         <b>One social card for the whole site</b>, drawn from the site's own mark, type and palette
///         (<c>assets/og-card.html</c>, rendered to <c>wwwroot/img/og-card.png</c>). It shipped with none for
///         a long time, on the reasoning that a machine-drawn card is worse than no card — true of a
///         placeholder, and the reason this one is a designed page rendered by a browser rather than
///         anything generated. Without an image a shared link unfurls as a line of text, and
///         <c>summary_large_image</c> — the card that takes the width of a feed — needs one. A card per
///         page would be a hundred and fifty images for a title the unfurl already prints beside it.
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

    /// <summary>The social card, as a path from the site root.</summary>
    public const string SocialImagePath = "/img/og-card.png";

    /// <summary>The social card's width in pixels — the 1.91:1 size every unfurler crops to.</summary>
    public const int SocialImageWidth = 1200;

    /// <summary>The social card's height in pixels.</summary>
    public const int SocialImageHeight = 630;

    /// <summary>What the social card shows, for a reader who cannot see it.</summary>
    public const string SocialImageAlt =
        "Rask — the .NET One Person Framework: build, run and ship a whole C# web app from one codebase on "
        + "one server.";

    /// <summary>The social card's absolute URL, which is the only form <c>og:image</c> accepts.</summary>
    public static string SocialImageUrl => Origin + LiveOptions.PathBase + SocialImagePath;

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
    ///     A route as a link on the site carries it: the path in its canonical, trailing-slash form, with any
    ///     query or fragment kept.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In-app links used to stay bare, on the grounds that the router never requests them and the
    ///         slash is noise in the address bar. That holds for a visitor clicking a <c>data-rask-nav</c>
    ///         link. It does not hold for a crawler, which requests every <c>href</c> it finds — so every
    ///         internal link on the site cost it a 301 before it reached the canonical, and the links, which
    ///         a search engine reads as a vote for a URL, voted for the redirecting form (#1057). A reload
    ///         already shows the slash, because that is the URL the host answers; now a click does too.
    ///     </para>
    ///     <para>
    ///         <c>NavLink</c>'s active match trims the slash before comparing, so a slashed link still
    ///         lights up on the page it names.
    ///     </para>
    /// </remarks>
    public static RouteUrl LinkTo(RouteUrl route)
    {
        string url = route;
        var tail = url.AsSpan().IndexOfAny('?', '#');
        return tail < 0 ? CanonicalPath(url) : CanonicalPath(url[..tail]) + url[tail..];
    }

    /// <summary>
    ///     The head block for one page: its title, its description, its canonical, the Open Graph and
    ///     Twitter tags built from the same three values and the site's card, and its JSON-LD graph.
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
        var image = SocialImageUrl;

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
            // The image and its sub-properties, in that order: a consumer reads og:image:* as describing the
            // og:image before it. The size is declared so an unfurler lays the card out before fetching it.
            Meta.Property("og:image").Content(image),
            Meta.Property("og:image:type").Content("image/png"),
            Meta.Property("og:image:width").Content(SocialImageWidth.ToString(CultureInfo.InvariantCulture)),
            Meta.Property("og:image:height").Content(SocialImageHeight.ToString(CultureInfo.InvariantCulture)),
            Meta.Property("og:image:alt").Content(SocialImageAlt),
            Meta.Name("twitter:card").Content("summary_large_image"),
            Meta.Name("twitter:title").Content(title),
            Meta.Name("twitter:description").Content(description),
            Meta.Name("twitter:image").Content(image),
            Meta.Name("twitter:image:alt").Content(SocialImageAlt),
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
