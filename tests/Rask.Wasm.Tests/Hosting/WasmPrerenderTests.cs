using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Wasm;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Hosting;

// Where a prerendered page lands, and why prerendering has to be asked for rather than inferred.
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
    public void PrerenderingIsOffUnlessItIsAskedFor()
    {
        // It cannot be inferred from a non-browser target framework: this assembly builds for net10.0
        // for its own tests, and those call RunAsync expecting a boot. Inferring it would turn every
        // one of them into a prerender pass.
        Assert.Null(Environment.GetEnvironmentVariable(WasmPrerender.OutputVariable));
        Assert.Null(WasmPrerender.RequestedOutput);
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
