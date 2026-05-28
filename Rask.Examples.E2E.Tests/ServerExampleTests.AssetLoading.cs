using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Server-only E2E for the per-component asset endpoint contract. The shared partial
// (SharedSmokeTests.AssetLoading) covers head-emission and lazy-mount behaviour that
// works identically on every host; this Server-only test exercises the dynamic
// /_rask/a/{hash}.{ext} endpoint by direct fetch. WASM hosts can't serve this endpoint
// from the live ScopedAssetRegistry — the WASM bundle runs in the browser, not the
// host process, so the host's registry never sees the registrations the browser made.
// Publish-time asset baking would lift the WASM restriction; tracked as a follow-up.
public sealed partial class ServerExampleTests
{
    [Fact]
    public Task AssetLoading_AssetEndpoint_ServesImmutablyCachedBytes() => RunAsync(async () =>
    {
        await NavigateToAsync("/asset-loading");
        await Expect(Page.Locator("main h1.h3")).ToHaveTextAsync("Asset loading",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var assetHref = await Page.Locator(
            "head link[rel='stylesheet'][href^='/_rask/a/']").First.GetAttributeAsync("href");
        Assert.NotNull(assetHref);
        Assert.Matches("^/_rask/a/[0-9a-f]{12}\\.css$", assetHref!);

        var response = await Page.APIRequest.GetAsync(assetHref);
        Assert.Equal(200, response.Status);
        Assert.True(response.Headers.TryGetValue("cache-control", out var cc),
            "Cache-Control header missing on asset response");
        Assert.Contains("immutable", cc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=31536000", cc, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.TryGetValue("etag", out var etag));
        Assert.Equal($"\"{ExtractHash(assetHref!)}\"", etag);
    });

    private static string ExtractHash(string href)
    {
        var lastSlash = href.LastIndexOf('/');
        var dot = href.IndexOf('.', lastSlash);
        return href.Substring(lastSlash + 1, dot - lastSlash - 1);
    }
}
