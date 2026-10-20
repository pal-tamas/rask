using System.Diagnostics;
using Rask.Core.Live;

#pragma warning disable RASK014 // the App is rendered directly as a root

namespace Rask.Site.Tests.Infrastructure;

/// <summary>The site's pages as the prerender pass writes them.</summary>
internal static class SiteDocument
{
    /// <summary>
    ///     The whole document a visitor at <paramref name="path" /> is served — rendered through the App and
    ///     its router, which is what the prerender pass does.
    /// </summary>
    public static async Task<string> At(string path)
    {
        var sp = TestServices.Default(routeState: TestRouteState.At(path));

        // The prerender engine itself, not Page.RenderDocument: these pages load on an async mount,
        // and a single synchronous render returns the placeholder — or the error page, for one that
        // awaits. Going through RenderDocument means this asserts on the same bytes the publish
        // writes, which is the only version of the page that a crawler ever sees.
        var started = Stopwatch.StartNew();
        var result = await RaskPrerender.RenderDocument(
            new global::Rask.Site.App(), sp, TimeSpan.FromSeconds(10));
        started.Stop();

        // A faulted render still returns perfectly ordinary HTML — the root boundary's error page, which
        // has a title and no description. Without this, every assertion would be made against
        // markup no visitor ever sees, and would fail saying nothing about the page.
        Assert.False(
            result.Faulted,
            $"{path} faulted while rendering: {result.Error?.GetType().Name}: {result.Error?.Message}");
        // Waves and time together say WHICH budget ran out: the wave cap in well under the ten seconds
        // means work kept being tracked, the full ten seconds means one wait never returned (#1108).
        Assert.False(
            result.TimedOut,
            $"{path} did not settle ({result.Waves} waves in {started.ElapsedMilliseconds} ms)");

        return result.Html;
    }
}
