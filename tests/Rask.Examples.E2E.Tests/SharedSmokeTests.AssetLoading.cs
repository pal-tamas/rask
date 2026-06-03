using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// E2E coverage for the /asset-loading showcase, exercising the per-component
// content-addressed CSS/JS pipeline against both the Server and WASM hosts.
// Runs identically on both fixtures because the AssetLoadingPage and its child
// components live in Rask.Example.Shared, and both hosts now expose the same
// /_rask/a/{hash}.{ext} endpoint backed by ScopedAssetRegistry.
public abstract partial class SharedSmokeTests
{
    private const string AssetLinkPattern = @"^/_rask/a/[0-9a-f]{12}\.css$";
    private const string AssetScriptPattern = @"^/_rask/a/[0-9a-f]{12}\.js$";

    [Fact]
    public Task AssetLoading_PageRenders() => RunAsync(async () =>
    {
        await NavigateToAsync("/asset-loading");
        await Expect(Page.Locator("main h1.h3")).ToHaveTextAsync("Asset loading",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    });

    [Fact]
    public Task AssetLoading_HeadHasPerComponentLinkTags() => RunAsync(async () =>
    {
        await NavigateToAsync("/asset-loading");
        // Wait for at least the page heading so we know the morph completed.
        await Expect(Page.Locator("main h1.h3")).ToHaveTextAsync("Asset loading",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Each scoped-CSS component on this page contributes one <link> with the
        // content-addressed href and the framework's rsk-css-{hash} morph key.
        var assetLinks = await Page.Locator(
            "head link[rel='stylesheet'][href^='/_rask/a/']").CountAsync();
        Assert.True(assetLinks >= 3,
            $"Expected at least 3 per-component asset links on /asset-loading (BasicScopedCss, TwinA, TwinB); got {assetLinks}");

        // The morph key prefix is the framework's reserved namespace — must be present
        // on every framework-emitted asset link so the keyed-morph reconciles by identity.
        var keyedLinks = await Page.Locator(
            "head link[data-rask-key^='rsk-css-']").CountAsync();
        Assert.True(keyedLinks >= 3,
            $"Expected at least 3 framework-keyed asset links; got {keyedLinks}");
    });

    [Fact]
    public Task AssetLoading_HeadHasJsOnlyScriptTag() => RunAsync(async () =>
    {
        await NavigateToAsync("/asset-loading");
        await Expect(Page.Locator("main h1.h3")).ToHaveTextAsync("Asset loading",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // The JsOnlyDemo component has only a sibling .js. Pre-cutover, JS-only
        // components silently dropped out of head emission (the mounted-set was
        // populated from a CSS-presence gate). Asserting the script presence is
        // a regression guard.
        var jsOnlyScripts = await Page.Locator(
            "head script[src^='/_rask/a/'][src$='.js']").CountAsync();
        Assert.True(jsOnlyScripts >= 1,
            $"Expected at least 1 JS asset script on /asset-loading (JsOnlyDemo); got {jsOnlyScripts}");
    });

    [Fact]
    public Task AssetLoading_LazyMount_AddsLinkOnShow_RemovesOnHide() => RunAsync(async () =>
    {
        await NavigateToAsync("/asset-loading");
        await Expect(Page.Locator("main h1.h3")).ToHaveTextAsync("Asset loading",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var assetLinkSelector = "head link[rel='stylesheet'][href^='/_rask/a/']";
        var initialCount = await Page.Locator(assetLinkSelector).CountAsync();

        // Toggle the lazy-mount button to mount LazyChild — its scoped CSS link should
        // appear in <head> via the keyed-morph insertion path.
        await Page.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { NameString = "Show LazyChild" }).ClickAsync();

        // Wait for the body element from LazyChild to appear, then assert the link count grew.
        await Expect(Page.Locator(".lazy-child")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var afterMount = await Page.Locator(assetLinkSelector).CountAsync();
        Assert.True(afterMount > initialCount,
            $"Expected lazy mount to add a CSS link (was {initialCount}, now {afterMount})");

        // Toggle back — the link should disappear from <head>.
        await Page.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { NameString = "Hide LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToHaveCountAsync(0);
        var afterUnmount = await Page.Locator(assetLinkSelector).CountAsync();
        Assert.Equal(initialCount, afterUnmount);
    });
}
