using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

#pragma warning disable RASK014 // the App is rendered directly as a root

namespace Rask.Site.Tests.Pages;

/// <summary>
///     Every routable page owes a crawler a title of its own, a description of its own, and a canonical
///     URL. These assert it, because the failure is silent in every other way.
/// </summary>
/// <remarks>
///     A page that forgets its description inherits the site-wide one from <c>App</c> — which renders,
///     validates, and quietly tells a search engine that twenty URLs are the same page. Nothing in a
///     browser looks wrong; the only symptom is a ranking, months later. So the check has to be a test.
/// </remarks>
public sealed class PageMetaTests
{
    /// <summary>
    ///     Every route a prerender pass would write, read from the same plan the pass reads.
    /// </summary>
    /// <remarks>
    ///     Routes rather than page types, for two reasons. A page is rendered THROUGH its layout — a
    ///     showcase page rendered as a document root faults, and the head that comes back is the error
    ///     page's, which would have made this whole file assert against markup no visitor ever sees. And
    ///     this is the same list <c>sitemap.xml</c> is built from, so "every page in the sitemap has
    ///     unique metadata" is the claim being made rather than an approximation of it.
    /// </remarks>
    public static TheoryData<string> PrerenderableRoutes()
    {
        var data = new TheoryData<string>();
        foreach (var path in AllRoutes())
        {
            data.Add(path);
        }

        return data;
    }

    private static IEnumerable<string> AllRoutes()
    {
        // Touching a page type is what runs the generated [Route] module initializers.
        _ = typeof(PwaPage);

        // The SUPPLIED paths as well as the planned ones, and the difference is most of the site.
        // PlanRoutes keeps the routes whose every segment is a literal, which is twenty pages; the
        // guides live behind one parameterised route and are supplied by GuidePrerenderPaths, which is
        // the other ~130. Leaving them out meant this file — and in particular the "did it settle"
        // assertion in HeadAt — never rendered the pages the publish actually has trouble with. A guide
        // that stalls or faults was visible only as a line in a publish log.
        return RaskPrerender.PlanRoutes().Paths
            .Concat(new GuidePrerenderPaths().Paths())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PrerenderableRoutes))]
    public async Task EveryRoutedPageNamesItselfAndSaysWhatItIs(string path)
    {
        var head = await HeadAt(path);

        Assert.Contains("<title", head, StringComparison.Ordinal);

        if (IsNoIndex(head))
        {
            // A page that has asked not to be indexed owes a crawler nothing more. The two routing-demo
            // targets are here: their whole body is one word, and giving them a description to satisfy
            // a test would be writing marketing copy for pages nobody should find.
            return;
        }

        Assert.Contains("name=\"description\"", head, StringComparison.Ordinal);

        // Not the App's. The singleton resolution means the LAST contributor wins, so a page that
        // declares nothing still renders a description — the App's — and passes a "has one" check. The
        // front door is the exception, and only it: the site's description IS that page's description.
        if (path != "/")
        {
            Assert.NotEqual(SiteIdentity.Description, WebUtility.HtmlDecode(DescriptionOf(head)));
        }
    }

    [Theory]
    [MemberData(nameof(PrerenderableRoutes))]
    public async Task EveryIndexablePageFitsItsTitleAndDescriptionInASearchResult(string path)
    {
        // A result shows about 60 characters of title and 160 of description, and cuts the rest with an
        // ellipsis. The front door's description was 250 — every result for it ended at "the same
        // components run on…". Under 110 wastes the two lines a result has to say what the page is.
        var head = await HeadAt(path);
        if (!ClaimsToBeThePage(head, path))
        {
            return;
        }

        var title = WebUtility.HtmlDecode(TitleOf(head));
        var description = WebUtility.HtmlDecode(DescriptionOf(head));

        Assert.True(title.Length <= 60, $"{path}'s title is {title.Length} characters: \"{title}\"");
        Assert.True(
            description.Length is >= 110 and <= 160,
            $"{path}'s description is {description.Length} characters (110–160): \"{description}\"");
    }

    [Theory]
    [MemberData(nameof(PrerenderableRoutes))]
    public async Task EveryIndexablePageSaysWhatItIsInOneStructuredDataGraph(string path)
    {
        var head = await HeadAt(path);
        if (IsNoIndex(head))
        {
            return;
        }

        // The '+' arrives as &#x2B; — the serializer's encoder escapes it in an attribute value, and a
        // parser decodes it back, which is what every crawler reading the DOM sees. Matching only the literal
        // form found zero graphs on a page that had one.
        var scripts = Regex.Matches(
            head,
            "<script[^>]*type=\"application/ld(?:\\+|&#x2B;)json\"[^>]*>(.*?)</script>",
            RegexOptions.Singleline);
        Assert.True(scripts.Count == 1, $"expected one JSON-LD graph at {path}, found {scripts.Count}");

        // Parsed, not pattern-matched: a graph with one stray quote is not a graph, and a crawler drops it
        // silently. JsonDocument is exactly as strict as the consumer.
        using var json = JsonDocument.Parse(scripts[0].Groups[1].Value);
        var graph = json.RootElement.GetProperty("@graph").EnumerateArray().ToList();
        var types = graph.Select(node => node.GetProperty("@type").GetString()).ToList();
        var canonical = CanonicalOf(head)!;

        Assert.Contains("WebSite", types);
        var page = Assert.Single(graph, node =>
            node.GetProperty("@type").GetString() is "WebPage" or "TechArticle"
            && node.GetProperty("url").GetString() == canonical);
        Assert.Equal(WebUtility.HtmlDecode(DescriptionOf(head)), page.GetProperty("description").GetString());

        if (canonical == PageMeta.Origin + "/")
        {
            // The one page that says what the software IS — and has no trail, being the start of it.
            Assert.Contains("SoftwareApplication", types);
            Assert.DoesNotContain("BreadcrumbList", types);
            return;
        }

        // The breadcrumb is the part of this a result visibly uses. It starts at the site and ends at the
        // page's own canonical, or it describes some other page.
        var crumbs = Assert.Single(graph, node => node.GetProperty("@type").GetString() == "BreadcrumbList")
            .GetProperty("itemListElement").EnumerateArray().ToList();
        Assert.Equal(PageMeta.Origin + "/", crumbs[0].GetProperty("item").GetString());
        Assert.Equal(canonical, crumbs[^1].GetProperty("item").GetString());
    }

    [Fact]
    public async Task AGuideIsAnArticleWithItsSectionItsDateAndItsMarkdownTwin()
    {
        var head = await HeadAt((string)Rask.Site.Features.Routes.GuidePage("cqrs"));
        var guide = GuideCatalog.Find("cqrs")!;
        var modified = GuideHistory.LastModified("cqrs");

        Assert.Contains("property=\"og:type\" content=\"article\"", head, StringComparison.Ordinal);
        Assert.Equal(guide.SearchTitle + PageMeta.TitleSuffix, WebUtility.HtmlDecode(TitleOf(head)));

        // Where an assistant gets the same guide without the page around it.
        var alternate = Regex.Match(head, "<link[^>]*type=\"text/markdown\"[^>]*>").Value;
        Assert.Contains($"href=\"{LlmsText.MarkdownUrl("cqrs")}\"", alternate, StringComparison.Ordinal);
        Assert.Contains("rel=\"alternate\"", alternate, StringComparison.Ordinal);

        // One date, stated three times: the meta tag the sitemap reads, and the article's dateModified.
        Assert.NotNull(modified);
        var iso = modified.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Matches($"property=\"article:modified_time\" content=\"{iso}\"", head);
        Assert.Contains($"\"dateModified\":\"{iso}\"", head, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PrerenderableRoutes))]
    public async Task EveryRoutedPageDeclaresOneCanonicalOnTheRealOrigin(string path)
    {
        var head = await HeadAt(path);
        var canonicals = Regex.Matches(head, "rel=\"canonical\"").Count;

        if (IsNoIndex(head))
        {
            // Nothing to be canonical about: the page has asked to stay out of the index entirely.
            return;
        }

        Assert.True(canonicals == 1, $"expected one canonical at {path}, found {canonicals}");

        // Matched on the two attributes separately rather than as one literal: Link writes href BEFORE
        // rel, and pinning the serializer's attribute order here would make this test fail on a change
        // that has nothing to do with what it is about.
        var canonical = Regex.Match(head, "<link[^>]*rel=\"canonical\"[^>]*>").Value;
        var target = Regex.Match(canonical, $"href=\"{Regex.Escape(PageMeta.Origin)}([^\"]*)\"");

        Assert.True(target.Success, $"{path}'s canonical does not point at {PageMeta.Origin}: {canonical}");

        // The form GitHub Pages serves without redirecting. The bare URL 301s to this one, so naming it
        // would have the page declare a canonical that redirects straight back to the page.
        Assert.EndsWith("/", target.Groups[1].Value, StringComparison.Ordinal);

        // It may point at ANOTHER route — /docs/todos/new canonicalises to /docs/todos, because the add
        // form is a state of the list rather than a page of its own, and consolidating them is exactly
        // what a canonical is for. What it may not do is point at a URL with no route behind it, which
        // is the mistake that costs more than having no canonical at all.
        Assert.Contains(target.Groups[1].Value.TrimEnd('/') is { Length: 0 } ? "/" : target.Groups[1].Value.TrimEnd('/'),
            AllRoutes());
    }

    [Fact]
    public async Task TheNotFoundPageIsNoindexAndHasNoCanonical()
    {
        // The one page that must NOT have one: it answers every unknown URL on the site, so a canonical
        // would point thousands of addresses at a single page, and an indexed 404 competes in search
        // results with the content the visitor was actually looking for.
        // Any address with no route behind it — which is what this page answers.
        var head = await HeadAt("/no-such-page-anywhere");

        Assert.DoesNotContain("rel=\"canonical\"", head, StringComparison.Ordinal);
        Assert.Contains("name=\"robots\"", head, StringComparison.Ordinal);
        Assert.Contains("noindex", head, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoTwoPagesShareATitleOrADescription()
    {
        // Duplicate titles and descriptions across a site are the two things a search console reports
        // by name. They are also exactly what a copy-pasted PageMeta.For call produces, which is how
        // this file's own sixteen call sites were written.
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in AllRoutes())
        {
            var head = await HeadAt(path);

            // Only the URLs that claim to BE a page. A noindex demo target has asked for nothing, and a
            // page whose canonical points elsewhere has said another URL is the real one — /docs/todos
            // and /docs/todos/new are one page with two addresses, so sharing a title is what they
            // should do. Same rule the prerender pass uses to decide what reaches sitemap.xml.
            //
            // This compared the canonical against the BARE path, and every canonical ends in a slash — so
            // it skipped every page on the site except "/" and asserted uniqueness over a set of one.
            if (!ClaimsToBeThePage(head, path))
            {
                continue;
            }

            var title = TitleOf(head);
            Assert.False(
                titles.TryGetValue(title, out var otherPath),
                $"{path} and {otherPath} share the title \"{title}\"");
            titles[title] = path;

            var description = DescriptionOf(head);
            Assert.False(
                descriptions.TryGetValue(description, out var otherDescriptionPath),
                $"{path} and {otherDescriptionPath} share a description");
            descriptions[description] = path;
        }
    }

    [Fact]
    public void EveryGuideIsHandedToThePrerenderPass()
    {
        // The guides are the site, and /docs/guides/{slug} is one route with no path of its own — so
        // without GuidePrerenderPaths the pass writes the twenty pages AROUND the content and skips the
        // content, on a publish that reports every route it knew about as written.
        var supplied = new GuidePrerenderPaths().Paths().ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(supplied);
        foreach (var guide in GuideCatalog.All)
        {
            Assert.Contains((string)Rask.Site.Features.Routes.GuidePage(guide.Slug), supplied);
        }
    }

    [Fact]
    public void TheCanonicalOriginMatchesTheOneTheSitemapIsBuiltFrom()
    {
        // PageMeta.Origin writes the canonicals; <RaskSiteUrl> in the csproj writes sitemap.xml. Two
        // constants naming the same thing drift, and a site whose sitemap and canonicals disagree about
        // its own address is telling a crawler two different things about every page.
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Site", "Rask.Site.csproj"));
        var declared = Regex.Match(csproj, "<RaskSiteUrl>([^<]+)</RaskSiteUrl>");

        Assert.True(declared.Success, "the csproj declares no <RaskSiteUrl>, so the publish writes no sitemap");
        Assert.Equal(PageMeta.Origin, declared.Groups[1].Value.TrimEnd('/'));

        // And the URL SHAPE, for the same reason. <RaskSiteTrailingSlash> is what the pass builds
        // sitemap.xml from; PageMeta.CanonicalPath is what goes in every canonical and og:url. A site
        // whose sitemap and canonicals disagree about the shape of its own URLs is telling a crawler two
        // different things about every page it has.
        var slash = Regex.Match(csproj, "<RaskSiteTrailingSlash>([^<]+)</RaskSiteTrailingSlash>");

        Assert.True(slash.Success, "the csproj does not say which URL form its host serves");
        Assert.Equal(
            bool.Parse(slash.Groups[1].Value),
            PageMeta.CanonicalPath("/docs/pwa").EndsWith('/'));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/docs", "/docs/")]
    [InlineData("/docs/", "/docs/")]
    [InlineData("/docs/pwa", "/docs/pwa/")]
    public void ACanonicalPathNamesWhatTheHostServes(string route, string expected)
    {
        // The root stays a single slash: it is already a directory URL, and doubling it names something
        // else. Everything below it gains one, because that is the URL GitHub Pages answers with 200 —
        // the bare form is a 301, checked against the live site rather than assumed.
        Assert.Equal(expected, PageMeta.CanonicalPath(route));
    }

    /// <summary>
    ///     The <c>&lt;head&gt;</c> a visitor at <paramref name="path" /> is served — rendered through the
    ///     App and its router, which is what the prerender pass does.
    /// </summary>
    private static async Task<string> HeadAt(string path)
    {
        var sp = TestServices.Default(routeState: TestRouteState.At(path));

        // The prerender engine itself, not RaskTest.RenderDocument: these pages load on an async mount,
        // and a single synchronous render returns the placeholder — or the error page, for one that
        // awaits. Going through RenderDocumentAsync means this asserts on the same bytes the publish
        // writes, which is the only version of the head that a crawler ever sees.
        var result = await RaskPrerender.RenderDocumentAsync(
            new global::Rask.Site.App(), sp, TimeSpan.FromSeconds(10));

        // A faulted render still returns perfectly ordinary HTML — the root boundary's error page, which
        // has a title and no description. Without this, every assertion below would be made against
        // markup no visitor ever sees, and would fail saying nothing about the page.
        Assert.False(
            result.Faulted,
            $"{path} faulted while rendering: {result.Error?.GetType().Name}: {result.Error?.Message}");
        Assert.False(result.TimedOut, $"{path} did not settle");

        var start = result.Html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        var end = result.Html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0 && end > start, $"{path} rendered no <head>");
        return result.Html[start..end];
    }

    private static string? CanonicalOf(string head)
    {
        var match = Regex.Match(head, "<link[^>]*rel=\"canonical\"[^>]*>");
        if (!match.Success)
        {
            return null;
        }

        var href = Regex.Match(match.Value, "href=\"([^\"]*)\"");
        return href.Success ? href.Groups[1].Value : null;
    }

    private static bool IsNoIndex(string head) =>
        Regex.IsMatch(head, "name=\"robots\"[^>]*noindex");

    /// <summary>
    ///     Whether the page at <paramref name="path" /> is indexable and names ITSELF as canonical — the pages
    ///     the sitemap lists, and the only ones whose title and description reach a search result.
    /// </summary>
    private static bool ClaimsToBeThePage(string head, string path) =>
        !IsNoIndex(head) && CanonicalOf(head) == PageMeta.Origin + PageMeta.CanonicalPath(path);

    private static string TitleOf(string head) =>
        Regex.Match(head, "<title[^>]*>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value;

    private static string DescriptionOf(string head) =>
        Regex.Match(head, "name=\"description\" content=\"([^\"]*)\"").Groups[1].Value;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
