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

    private sealed class Broken : Component
    {
        protected override Component? Render() => throw new InvalidOperationException("boom");
    }
}
