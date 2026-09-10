using Rask.Core.Live;

namespace Rask.Site;

/// <summary>
///     The head metadata every page owes a crawler: a title of its own, a description of its own, and a
///     canonical URL saying which address is the real one.
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
    ///     The head block for one page: its title, its description, its canonical, and the Open Graph
    ///     and Twitter tags built from the same three values.
    /// </summary>
    /// <param name="title">
    ///     The page's own title. Rendered as-is, so it should carry the site name itself — "Todos —
    ///     Rask" rather than "Todos".
    /// </param>
    /// <param name="description">
    ///     One or two sentences describing THIS page. Search engines truncate around 155 characters, so
    ///     the first sentence has to stand on its own.
    /// </param>
    /// <param name="path">
    ///     The page's route, as a rooted path. Pass a generated <c>Routes.X()</c> rather than a literal:
    ///     a canonical pointing at a URL that has no route behind it is the one SEO mistake that costs
    ///     more than having no canonical at all, and a literal is exactly how the sidebar's fourteen
    ///     entries became dead links when the showcase moved under /docs.
    /// </param>
    public static Component For(string title, string description, string path)
    {
        var url = Origin + LiveOptions.PathBase + CanonicalPath(path);

        return
        [
            Title[title],
            Meta.Name("description").Content(description),
            Link.Rel("canonical").Href(url),
            Meta.Property("og:type").Content("website"),
            Meta.Property("og:site_name").Content("Rask"),
            Meta.Property("og:title").Content(title),
            Meta.Property("og:description").Content(description),
            Meta.Property("og:url").Content(url),
            Meta.Name("twitter:card").Content("summary"),
            Meta.Name("twitter:title").Content(title),
            Meta.Name("twitter:description").Content(description),
        ];
    }
}
