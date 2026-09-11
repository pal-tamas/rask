using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Hosting;

// Where a prerendered page lands, and why prerendering has to be asked for rather than inferred.
// Serialised because PrerenderingIsOffUnlessItIsAskedFor asserts a process-wide environment variable
// is unset, and PrerenderBatteryWiringTests sets it.
[Collection("RaskPrerenderEnvironment")]
public class WasmPrerenderTests
{
    [Fact]
    public void TheRootGoesToTheDirectorysOwnIndex()
    {
        Assert.Equal(
            Path.Combine("out", "index.html"),
            WasmPrerender.OutputPathFor("out", "/"));
    }

    [Theory]
    [InlineData("/about", "about")]
    [InlineData("/guides/intro", "guides/intro")]
    public void EveryOtherRouteGetsADirectoryOfItsOwn(string route, string expectedDirectory)
    {
        // Directory-per-route rather than about.html, so a static host serves the page at the URL the
        // app routes to — no extension in it, and no per-host rewrite rule to configure. Getting this
        // wrong produces a site that 404s at every link while every file is present on disk.
        var expected = Path.Combine("out", Path.Combine(expectedDirectory.Split('/')), "index.html");

        Assert.Equal(expected, WasmPrerender.OutputPathFor("out", route));
    }

    [Fact]
    public void ARouteWithATrailingSlashLandsInTheSamePlaceAsOneWithout()
    {
        Assert.Equal(
            WasmPrerender.OutputPathFor("out", "/about"),
            WasmPrerender.OutputPathFor("out", "/about/"));
    }

    [Fact]
    public async Task ItWritesAPageForEveryPrerenderableRouteAndSkipsTheRest()
    {
        // The end-to-end shape: routes in, files on disk. Called directly rather than through the
        // environment variable, because that variable is process-global and this assembly runs its
        // classes in parallel — the same race that made the diagnostics-sink tests flaky.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(ItWritesAPageForEveryPrerenderableRouteAndSkipsTheRest), [
            new RouteRegistration(typeof(Home), "/", null),
            new RouteRegistration(typeof(Home), "/about", null),
            new RouteRegistration(typeof(Home), "/products/{id}", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            // The count is deliberately not asserted: RouteRegistry is process-global and Replace
            // swaps only this group, so the plan also carries whatever other tests have registered.
            // Asserting a total here would fail depending on what else ran, which is a worse test than
            // none — the claim is about THESE routes.
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(Path.Combine(dir, "index.html")), "the root was not written");
            Assert.True(File.Exists(Path.Combine(dir, "about", "index.html")), "/about was not written");

            // The parameterised route has no path without data, so nothing is written for it — and
            // nothing is invented for it either.
            Assert.False(Directory.Exists(Path.Combine(dir, "products")));

            // What landed is a real document, not a fragment.
            var home = await File.ReadAllTextAsync(Path.Combine(dir, "index.html"));
            Assert.Contains("<!doctype html>", home, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("home-page", home, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task APageThatThrowsIsNotWritten()
    {
        // A root boundary renders an error document, which is perfectly ordinary HTML — writing it
        // would publish an error page under the route's own name and nothing would say so. The bundle
        // still serves the route at runtime, so skipping loses nothing.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(APageThatThrowsIsNotWritten), [
            new RouteRegistration(typeof(Broken), "/broken", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            await WasmPrerender.RunAsync<Broken>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(Path.Combine(dir, "broken", "index.html")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task AnAppCanSupplyThePathsAParameterisedRouteExpandsTo()
    {
        // The gap this closes: a docs site's /guides/{slug} is ONE route and eighty pages, and the pass
        // cannot know the slugs. Without a way to say, the whole of a site's content ships to a crawler
        // as a boot shell while the publish reports every page it knew about as written — a green build
        // whose only symptom is a small sitemap.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(AnAppCanSupplyThePathsAParameterisedRouteExpandsTo), [
            new RouteRegistration(typeof(Home), "/", null),
            new RouteRegistration(typeof(Home), "/guides/{slug}", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();
        services.AddSingleton<IPrerenderPaths>(new FixedPaths("/guides/intro", "/guides/routing"));

        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(Path.Combine(dir, "guides", "intro", "index.html")));
            Assert.True(File.Exists(Path.Combine(dir, "guides", "routing", "index.html")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task ASuppliedPathThatIsAlreadyALiteralRouteIsNotRenderedTwice()
    {
        // Two registrations naming the same page is a mistake with no error attached: the second render
        // simply overwrites the first, and the only trace is a URL listed twice in the sitemap — which a
        // crawler reads as a malformed file rather than as a duplicate.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(ASuppliedPathThatIsAlreadyALiteralRouteIsNotRenderedTwice), [
            new RouteRegistration(typeof(Home), "/about", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();
        // The same path twice over, once from each of two sources, and once already in the plan.
        services.AddSingleton<IPrerenderPaths>(new FixedPaths("/about", "/extra"));
        services.AddSingleton<IPrerenderPaths>(new FixedPaths("/extra"));

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");
        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var sitemap = await File.ReadAllTextAsync(Path.Combine(dir, "sitemap.xml"));

            Assert.Equal(1, Occurrences(sitemap, "<loc>https://example.com/about/</loc>"));
            Assert.Equal(1, Occurrences(sitemap, "<loc>https://example.com/extra/</loc>"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private sealed class FixedPaths(params string[] paths) : IPrerenderPaths
    {
        public IEnumerable<string> Paths() => paths;
    }

    [Fact]
    public async Task ASitemapListsEveryPageThatWasWritten()
    {
        // Absolute URLs, because that is what the sitemap protocol says and a crawler discards a
        // sitemap of relative paths. Nothing in a static publish knows the origin, so the app names it.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(ASitemapListsEveryPageThatWasWritten), [
            new RouteRegistration(typeof(Home), "/", null),
            new RouteRegistration(typeof(Home), "/about", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com/");
        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var sitemap = await File.ReadAllTextAsync(Path.Combine(dir, "sitemap.xml"));

            Assert.Contains("<loc>https://example.com/</loc>", sitemap, StringComparison.Ordinal);

            // With the slash, because that is the URL a host serving {route}/index.html answers with
            // 200 — GitHub Pages 301s the bare one. A sitemap of redirects is reported as such.
            Assert.Contains("<loc>https://example.com/about/</loc>", sitemap, StringComparison.Ordinal);

            // The trailing slash on the configured origin must not survive into the URLs, or every
            // entry is a double slash that redirects — which a crawler treats as a different URL.
            Assert.DoesNotContain("https://example.com//", sitemap, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Theory]
    // The root is already a directory URL; doubling the slash names something else.
    [InlineData("/", true, "/")]
    [InlineData("/", false, "/")]
    // Everything below it takes the host's shape.
    [InlineData("/about", true, "/about/")]
    [InlineData("/about", false, "/about")]
    // Idempotent, so a supplied path that already carries one does not gain a second.
    [InlineData("/about/", true, "/about/")]
    [InlineData("/about/", false, "/about")]
    public void ASitemapUrlTakesTheShapeTheHostServes(string path, bool trailingSlash, string expected) =>
        Assert.Equal(expected, WasmPrerender.SiteUrlPath(path, trailingSlash));

    [Fact]
    public async Task AHostThatStripsTheSlashGetsUrlsWithoutOne()
    {
        // Netlify and Cloudflare Pages normalise the other way. Whichever way a host goes, naming the
        // other form points every URL in the sitemap at a redirect — so this is a stated choice rather
        // than a convention the build guesses at.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(AHostThatStripsTheSlashGetsUrlsWithoutOne), [
            new RouteRegistration(typeof(Home), "/about", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");
        Environment.SetEnvironmentVariable(WasmPrerender.TrailingSlashVariable, "false");
        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var sitemap = await File.ReadAllTextAsync(Path.Combine(dir, "sitemap.xml"));

            Assert.Contains("<loc>https://example.com/about</loc>", sitemap, StringComparison.Ordinal);
            Assert.DoesNotContain("/about/</loc>", sitemap, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            Environment.SetEnvironmentVariable(WasmPrerender.TrailingSlashVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task ARouteThatWasNotWrittenStaysOutOfTheSitemap()
    {
        // The claim that makes the sitemap worth having: it is built from what reached disk, not from
        // the route table. A skipped route still ANSWERS — with the boot shell — so listing it points a
        // crawler at exactly the blank page prerendering exists to stop it seeing.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(ARouteThatWasNotWrittenStaysOutOfTheSitemap), [
            new RouteRegistration(typeof(BrokenOnOneRoute), "/fine", null),
            new RouteRegistration(typeof(BrokenOnOneRoute), "/broken", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");
        try
        {
            await WasmPrerender.RunAsync<BrokenOnOneRoute>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            // The premise: one of the two really did fail. Without this the assertion below would pass
            // just as well on a sitemap that listed neither.
            Assert.True(File.Exists(Path.Combine(dir, "fine", "index.html")), "/fine was not written");
            Assert.False(File.Exists(Path.Combine(dir, "broken", "index.html")), "/broken WAS written");

            var sitemap = await File.ReadAllTextAsync(Path.Combine(dir, "sitemap.xml"));

            Assert.Contains("/fine", sitemap, StringComparison.Ordinal);
            Assert.DoesNotContain("/broken", sitemap, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Theory]
    // Nothing to say about indexing: listed.
    [InlineData("<html><head><title>x</title></head><body/></html>", "/a", true)]
    // Asked not to be indexed: written, but a sitemap is a request to INDEX, so listing it submits a
    // contradiction — which Search Console reports against the whole file, not the one URL.
    [InlineData("<head><meta name=\"robots\" content=\"noindex, follow\"></head>", "/a", false)]
    // Says another URL is the real one. An add form canonicalising to its list is the ordinary case.
    [InlineData("<head><link href=\"https://x.test/list\" rel=\"canonical\"></head>", "/list/new", false)]
    // Says THIS is the real one, which is the common case and must not be excluded by the rule above.
    [InlineData("<head><link href=\"https://x.test/list\" rel=\"canonical\"></head>", "/list", true)]
    // A trailing slash is not a different page: a static host serves both from the same file.
    [InlineData("<head><link href=\"https://x.test/list/\" rel=\"canonical\"></head>", "/list", true)]
    public void ASitemapListsOnlyTheUrlsThatClaimToBeAPage(string html, string path, bool listed) =>
        Assert.Equal(listed, WasmPrerender.ListedInSitemap(html, path));

    [Fact]
    public void ACanonicalIsReadFromItsOwnTagAndNotANeighbours()
    {
        // The bug a looser reader has: scanning for rel="canonical" and then for the next href="…"
        // picks up the FOLLOWING link, so a page whose canonical is written rel-first silently
        // canonicalises to its stylesheet.
        const string Head =
            "<head><link rel=\"canonical\" href=\"https://x.test/page\">"
            + "<link rel=\"stylesheet\" href=\"/a.css\"></head>";

        Assert.Equal("https://x.test/page", WasmPrerender.CanonicalTarget(Head));
    }

    [Theory]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"2026-09-10\"></head>", "2026-09-10")]
    // Attribute order is the serializer's business, and it writes an offset's '+' as an entity.
    [InlineData("<head><meta content=\"2026-09-10T08:30:00&#x2B;02:00\" property=\"article:modified_time\"></head>",
        "2026-09-10T08:30:00+02:00")]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"2026-09-10T06:30:00Z\"></head>",
        "2026-09-10T06:30:00+00:00")]
    // The coarser W3C profiles are dates too, and are passed through as written.
    [InlineData("<head><meta property=\"article:modified_time\" content=\"2026-09\"></head>", "2026-09")]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"2026\"></head>", "2026")]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"2026-09-10T08:30\"></head>",
        "2026-09-10T08:30:00+00:00")]
    // Not a date. No lastmod beats a malformed one, which is reported against the whole sitemap.
    [InlineData("<head><meta property=\"article:modified_time\" content=\"last Tuesday\"></head>", null)]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"10\"></head>", null)]
    // Dates only a culture can read. Which day "01/02/2026" is depends on who wrote it, so it is not one.
    [InlineData("<head><meta property=\"article:modified_time\" content=\"01/02/2026\"></head>", null)]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"Sep 10, 2026\"></head>", null)]
    [InlineData("<head><meta property=\"article:modified_time\" content=\"2026-13\"></head>", null)]
    // A different property, and no date at all: nothing to say, so nothing said.
    [InlineData("<head><meta property=\"og:updated_time\" content=\"2026-09-10\"></head>", null)]
    [InlineData("<head><title>x</title></head>", null)]
    public void ALastmodIsReadOffThePagesOwnModifiedTime(string html, string? expected) =>
        Assert.Equal(expected, WasmPrerender.LastModified(html));

    [Fact]
    public void AModifiedTimeIsReadFromItsOwnTagAndNotANeighbours()
    {
        // The same trap as the canonical reader: a tag with no content followed by one with a date must not
        // lend the first one the second one's date.
        const string Head =
            "<head><meta property=\"article:modified_time\"><meta name=\"x\" content=\"2020-01-01\"></head>";

        Assert.Null(WasmPrerender.LastModified(Head));
    }

    [Fact]
    public async Task ASitemapCarriesTheDateAPageStatesAndNoneForAPageThatStatesNone()
    {
        // The page is the one place its date is stated. A page with none gets a <url> with no <lastmod> —
        // never the time of the publish, which would mark every URL changed on every deploy.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(ASitemapCarriesTheDateAPageStatesAndNoneForAPageThatStatesNone), [
            new RouteRegistration(typeof(DatedOnOneRoute), "/dated", null),
            new RouteRegistration(typeof(DatedOnOneRoute), "/plain", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");
        try
        {
            await WasmPrerender.RunAsync<DatedOnOneRoute>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var sitemap = await File.ReadAllTextAsync(Path.Combine(dir, "sitemap.xml"));

            Assert.Contains(
                "<url><loc>https://example.com/dated/</loc><lastmod>2026-09-10</lastmod></url>",
                sitemap,
                StringComparison.Ordinal);
            Assert.Contains("<url><loc>https://example.com/plain/</loc></url>", sitemap, StringComparison.Ordinal);
            Assert.Equal(1, Occurrences(sitemap, "<lastmod>"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void APageWithNoRobotsMetaIsIndexable()
    {
        // The negative control for IsNoIndex. A reader that matched too loosely — on the word "noindex"
        // anywhere in the document, say — would drop every page that DOCUMENTS the tag, which on this
        // repo's own site is a guide.
        Assert.False(WasmPrerender.IsNoIndex("<head><title>Using noindex</title></head>"));
        Assert.False(WasmPrerender.IsNoIndex("<head><meta name=\"robots\" content=\"index, follow\"></head>"));
        Assert.True(WasmPrerender.IsNoIndex("<head><meta name=\"robots\" content=\"noindex\"></head>"));
    }

    [Fact]
    public async Task NoOriginMeansNoSitemapRatherThanAGuessedOne()
    {
        // A domain guessed into a published file is worse than no sitemap: it is wrong on every host
        // but one, and nothing about the output says it was invented.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(NoOriginMeansNoSitemapRatherThanAGuessedOne), [
            new RouteRegistration(typeof(Home), "/", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(Path.Combine(dir, "sitemap.xml")));
            Assert.False(File.Exists(Path.Combine(dir, "robots.txt")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task RobotsPointsAtTheSitemap_ButNeverOverwritesTheAppsOwn()
    {
        // robots.txt has real consequences — a wrong one delists a site — so an author who shipped one
        // has said something this pass has no business editing.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(RobotsPointsAtTheSitemap_ButNeverOverwritesTheAppsOwn), [
            new RouteRegistration(typeof(Home), "/", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");
        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var written = await File.ReadAllTextAsync(Path.Combine(dir, "robots.txt"));
            Assert.Contains("Sitemap: https://example.com/sitemap.xml", written, StringComparison.Ordinal);

            // Now the app's own, on a second pass over the same directory.
            await File.WriteAllTextAsync(Path.Combine(dir, "robots.txt"), "User-agent: *\nDisallow: /\n");
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.Equal(
                "User-agent: *\nDisallow: /\n",
                await File.ReadAllTextAsync(Path.Combine(dir, "robots.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void APathBaseFromTheBuildReachesTheRenderedUrls()
    {
        // A browser boot reads the prefix off the document's <base href>. A prerender pass has no
        // document, so without the build saying, every PathBase-prefixed URL is baked against an empty
        // prefix — `/_rask/a/x.css` rather than `/docs/_rask/a/x.css`. A <base href> cannot rescue
        // those: it applies to RELATIVE URLs only, and a leading slash means the origin root. A sub-path
        // deploy then serves pages that ask the origin root for their own scoped assets, which is how
        // this was found — every WASM sub-path journey went red at once.
        var previous = LiveOptions.PathBase;
        try
        {
            LiveOptions.PathBase = "";
            Environment.SetEnvironmentVariable(WasmPrerender.PathBaseVariable, "/docs");

            WasmPrerender.ApplyPathBase();

            Assert.Equal("/docs", LiveOptions.PathBase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.PathBaseVariable, null);
            LiveOptions.PathBase = previous;
        }
    }

    [Fact]
    public void AnExplicitPathBaseIsNotOverruledByTheBuild()
    {
        // A host configured with an explicit PathBase in Program.cs has said something more specific
        // than a publish flag. This matches the browser boot, where an explicit value also wins over
        // the <base href> auto-detect.
        var previous = LiveOptions.PathBase;
        try
        {
            LiveOptions.PathBase = "/chosen";
            Environment.SetEnvironmentVariable(WasmPrerender.PathBaseVariable, "/docs");

            WasmPrerender.ApplyPathBase();

            Assert.Equal("/chosen", LiveOptions.PathBase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.PathBaseVariable, null);
            LiveOptions.PathBase = previous;
        }
    }

    [Fact]
    public void PrerenderingIsOffUnlessItIsAskedFor()
    {
        // It cannot be inferred from a non-browser target framework: this assembly builds for net10.0
        // for its own tests, and those call RunAsync expecting a boot. Inferring it would turn every
        // one of them into a prerender pass.
        Assert.Null(Environment.GetEnvironmentVariable(WasmPrerender.OutputVariable));
        Assert.Null(WasmPrerender.RequestedOutput);
    }

    [Fact]
    public async Task APageWrittenBesideABootShellIsSplicedIntoItRatherThanOverIt()
    {
        // The gap every other test in this file walked past. They render into an EMPTY temp directory,
        // which is the one arrangement where there is no shell to destroy — so a pass that overwrote
        // the published boot shell, and shipped a page that could never become interactive, passed
        // them all. Here the shell is on disk first, exactly as it is in a real published wwwroot.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        RouteRegistry.Replace(nameof(APageWrittenBesideABootShellIsSplicedIntoItRatherThanOverIt), [
            new RouteRegistration(typeof(Home), "/", null),
            new RouteRegistration(typeof(Home), "/about", null),
        ]);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "index.html"),
            """
            <!doctype html><html lang="en"><head><meta charset="utf-8"/><base href="/"/><title>Rask</title>
            <script type="importmap">{"imports":{}}</script></head>
            <body data-rask-root><div class="rask-boot">Loading…</div>
            <script src="main.js" type="module"></script></body></html>
            """);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var root = await File.ReadAllTextAsync(Path.Combine(dir, "index.html"));

            Assert.Contains("home-page", root, StringComparison.Ordinal);
            Assert.Contains("<script src=\"main.js\" type=\"module\">", root, StringComparison.Ordinal);
            Assert.Contains("type=\"importmap\"", root, StringComparison.Ordinal);
            Assert.Contains("<base href=\"/\"/>", root, StringComparison.Ordinal);

            // Every route gets the shell, not just the root one — a sub-page without the boot script
            // is a dead end, and it is the page a search result links to.
            var about = await File.ReadAllTextAsync(Path.Combine(dir, "about", "index.html"));
            Assert.Contains("home-page", about, StringComparison.Ordinal);
            Assert.Contains("<script src=\"main.js\" type=\"module\">", about, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task TheShellIsReadOnceSoTheRootPageIsNotUsedAsTheNextPagesShell()
    {
        // The root route's output IS index.html — the same file the shell is read from. Reading it per
        // page would hand page two a shell that already contains page one's markup, and every page
        // after the first would accumulate the ones before it.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        RouteRegistry.Replace(nameof(TheShellIsReadOnceSoTheRootPageIsNotUsedAsTheNextPagesShell), [
            new RouteRegistration(typeof(Home), "/", null),
            new RouteRegistration(typeof(Home), "/about", null),
        ]);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "index.html"),
            """
            <!doctype html><html><head><title>Rask</title></head>
            <body><div class="rask-boot">Loading…</div><script src="main.js" type="module"></script></body></html>
            """);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var about = await File.ReadAllTextAsync(Path.Combine(dir, "about", "index.html"));

            Assert.Equal(1, Occurrences(about, "home-page"));
            Assert.Equal(1, Occurrences(about, "main.js"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task TheUntouchedShellIsKeptAsANeutralFallbackForRoutesThatWereNotPrerendered()
    {
        // #974. Prerendering breaks deep links to un-prerenderable routes by building the very thing
        // meant to help: the root route's output IS index.html, so after this pass the file a static
        // host falls back to is no longer a neutral shell — it is the HOME PAGE, fully rendered. A deep
        // link to a route that could not be prerendered then boots into a document already describing a
        // different page. Before prerendering, that same link got an empty shell and routed correctly.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        RouteRegistry.Replace(nameof(TheUntouchedShellIsKeptAsANeutralFallbackForRoutesThatWereNotPrerendered), [
            new RouteRegistration(typeof(Home), "/", null),
        ]);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "index.html"),
            """
            <!doctype html><html lang="en"><head><meta charset="utf-8"/><base href="/"/><title>Rask</title>
            <script type="importmap">{"imports":{}}</script></head>
            <body data-rask-root><div class="rask-boot">Loading…</div>
            <script src="main.js" type="module"></script></body></html>
            """);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            var fallback = await File.ReadAllTextAsync(Path.Combine(dir, "404.html"));

            // It can boot — the import map, the base href and the boot script are all there.
            Assert.Contains("<script src=\"main.js\" type=\"module\">", fallback, StringComparison.Ordinal);
            Assert.Contains("type=\"importmap\"", fallback, StringComparison.Ordinal);
            Assert.Contains("<base href=\"/\"/>", fallback, StringComparison.Ordinal);

            // And it is NEUTRAL. This is the whole assertion: the home page was prerendered into
            // index.html in this very run, and none of it may appear here.
            Assert.DoesNotContain("home-page", fallback, StringComparison.Ordinal);

            // The root page really was prerendered, so the check above is not passing because nothing
            // happened.
            Assert.Contains("home-page", await File.ReadAllTextAsync(Path.Combine(dir, "index.html")), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task NoBootShellMeansNoFallbackIsInvented()
    {
        // With no shell to copy there is nothing neutral to write, and writing a whole prerendered
        // document as 404.html would be worse than writing none: a static host would serve the home
        // page for every unknown path, which is the failure this fixes rather than a lesser version
        // of it.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        RouteRegistry.Replace(nameof(NoBootShellMeansNoFallbackIsInvented), [
            new RouteRegistration(typeof(Home), "/", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(Path.Combine(dir, "404.html")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task ItsOwnRobotsIsRewrittenRatherThanMistakenForTheApps()
    {
        // The same family as #1036, quieter: the second publish into a directory finds the FIRST
        // publish's robots.txt, and "a robots.txt exists" was read as "the author shipped one". So an
        // app that changed its origin kept publishing a file pointing a crawler at the old domain's
        // sitemap — with the log saying the author had asked for it.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(nameof(ItsOwnRobotsIsRewrittenRatherThanMistakenForTheApps), [
            new RouteRegistration(typeof(Home), "/", null),
        ]);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://staging.example.com");
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, "https://example.com");
            await WasmPrerender.RunAsync<Home>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));

            Assert.Equal(
                "User-agent: *\nAllow: /\nSitemap: https://example.com/sitemap.xml\n",
                await File.ReadAllTextAsync(Path.Combine(dir, "robots.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.SiteUrlVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task PublishingTwiceIntoTheSameDirectoryWritesTheSameBytes()
    {
        // #1036. The pass runs AfterTargets="Publish" and writes into the published wwwroot, where the
        // root route's own output IS index.html — the file the shell is read from. Publish twice into
        // the same directory and the SDK does not rescue it: the prerendered index.html is NEWER than
        // the staged shell, so the copy step calls it up to date and leaves it alone. The second pass
        // then read a merged page as its shell and spliced the head into a document that already had
        // it, so every stylesheet, preload, meta and canonical appeared twice — and a third publish
        // made three. Silently: green build, page renders, and only something COUNTING the elements
        // sees it.
        //
        // Asserted as byte equality over the whole directory rather than on one tag, because the
        // duplication is not the only way a second pass can differ from the first, and "publish twice
        // = publish once" is the property that actually has to hold.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        RouteRegistry.Replace(nameof(PublishingTwiceIntoTheSameDirectoryWritesTheSameBytes), [
            new RouteRegistration(typeof(HeadContributor), "/", null),
            new RouteRegistration(typeof(HeadContributor), "/about", null),
        ]);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "index.html"),
            """
            <!doctype html><html lang="en"><head><meta charset="utf-8"/><base href="/"/><title>Rask</title>
            <script type="importmap">{"imports":{}}</script></head>
            <body data-rask-root><div class="rask-boot">Loading…</div>
            <script src="main.js" type="module"></script></body></html>
            """);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            await WasmPrerender.RunAsync<HeadContributor>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));
            var first = Snapshot(dir);

            await WasmPrerender.RunAsync<HeadContributor>(
                services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5));
            var second = Snapshot(dir);

            // Named individually before the whole-directory compare, so a failure says WHICH page grew
            // rather than only that something did.
            foreach (var page in new[] { "index.html", Path.Combine("about", "index.html") })
            {
                var html = await File.ReadAllTextAsync(Path.Combine(dir, page));
                Assert.Equal(1, Occurrences(html, "name=\"description\""));
                Assert.Equal(1, Occurrences(html, "property=\"og:title\""));
                Assert.Equal(1, Occurrences(html, "type=\"importmap\""));
                Assert.Equal(1, Occurrences(html, "data-rask-prerendered"));
            }

            Assert.Equal(first, second);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task ARenderedPageWithNoPristineShellBesideItIsRefusedRatherThanMergedInto()
    {
        // The other half of #1036: the recovery reads the untouched shell the pass keeps at 404.html,
        // and a directory that has the rendered index.html but not that copy has nothing to recover
        // from. Merging anyway is what duplicated the head in the first place, and writing whole
        // documents instead would publish pages that can never boot — so this stops, and says how to
        // get out of it. A silent wrong answer is the thing being fixed, not a fallback to keep.
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        RouteRegistry.Replace(nameof(ARenderedPageWithNoPristineShellBesideItIsRefusedRatherThanMergedInto), [
            new RouteRegistration(typeof(Home), "/", null),
        ]);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "index.html"),
            """
            <!doctype html><html lang="en" data-rask-prerendered><head><title>Rask</title></head>
            <body><div class="home-page"></div><script src="main.js" type="module"></script></body></html>
            """);

        var services = new ServiceCollection();
        services.AddScoped<RouteState>();

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WasmPrerender.RunAsync<Home>(
                    services.BuildServiceProvider(), dir, TimeSpan.FromSeconds(5)));

            Assert.Contains("404.html", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>Every file under a directory, by relative path, with its bytes hashed.</summary>
    private static Dictionary<string, string> Snapshot(string root)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            files[relative] = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        }

        return files;
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var cursor = 0;
        while (true)
        {
            var hit = haystack.IndexOf(needle, cursor, StringComparison.Ordinal);
            if (hit < 0)
            {
                return count;
            }

            count++;
            cursor = hit + needle.Length;
        }
    }

    private sealed class Home : Component
    {
        protected override Component? Render() => Div["home-page"];
    }

    // A page that contributes to <head>, which is what the merge duplicates. A component with no head
    // assets cannot show the #1036 failure at all: the shell's head is the only head there is.
    private sealed class HeadContributor : Component
    {
        protected override Component? HeadAssets =>
        [
            Title["Home"],
            Meta.Name("description").Content("a page"),
            Meta.Property("og:title").Content("Home"),
        ];

        protected override Component? Render() => Div["home-page"];
    }

    /// <summary>States a modified time on one route and none on the others.</summary>
    /// <remarks>
    ///     Reads the route itself for the reason <see cref="BrokenOnOneRoute" /> does: the pass renders the same
    ///     app for every path.
    /// </remarks>
    private sealed class DatedOnOneRoute(RouteState route) : Component
    {
        protected override Component? HeadAssets =>
            route.Path == "/dated" ? Meta.Property("article:modified_time").Content("2026-09-10") : null;

        protected override Component? Render() => Div["page"];
    }

    private sealed class Broken : Component
    {
        protected override Component? Render() => throw new InvalidOperationException("boom");
    }

    /// <summary>Renders for every route but one, which it throws on.</summary>
    /// <remarks>
    ///     RunAsync&lt;TApp&gt; instantiates the SAME app for every path — the app's own router is what
    ///     picks the page — so a fixture cannot make one route throw by registering a throwing component
    ///     against it. It has to read the route itself, which is what this does.
    /// </remarks>
    private sealed class BrokenOnOneRoute(RouteState route) : Component
    {
        protected override Component? Render() =>
            route.Path == "/broken" ? throw new InvalidOperationException("boom") : Div["home-page"];
    }
}
