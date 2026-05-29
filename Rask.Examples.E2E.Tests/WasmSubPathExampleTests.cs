using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     Sub-path E2E smoke. Boots Rask.Example.Wasm.Host with <c>pathBase: "/sub"</c>
///     backed by a Rask.Example.Wasm AppBundle published with
///     <c>/p:RaskPathBase=/sub</c>. Verifies the page boots under the prefix, that
///     scoped-CSS asset URLs honor the prefix, and that an unprefixed root request
///     misses (proving the framework endpoints actually moved, not duplicated).
///     This is the runtime regression guard for the GH Pages sub-path bug fix —
///     the back-end endpoint routing is unit-tested in Rask.Wasm.Hosting.Tests.
/// </summary>
[Collection(SubPathWasmExampleCollection.Name)]
public sealed class WasmSubPathExampleTests : IAsyncLifetime
{
    private readonly SubPathWasmAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext? _context;
    private IPage? _page;

    public WasmSubPathExampleTests(SubPathWasmAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    private IPage Page => _page
        ?? throw new InvalidOperationException("Test page accessed before InitializeAsync ran");

    public async Task InitializeAsync()
    {
        // BaseURL is the origin (no prefix). Tests pass "/sub/..." explicitly so
        // Playwright's absolute-path resolution doesn't drop the prefix — calling
        // Page.GotoAsync("/") with BaseURL "http://host/sub" goes to "http://host/"
        // (the leading "/" is absolute, not relative to the base path).
        _context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _app.OriginUrl
        });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_page is not null)
        {
            try { await TestArtifacts.DumpAsync(_page, "SubPathWasm", "after-test", _app.ServerLog); }
            catch { /* best effort */ }
            await _page.CloseAsync();
        }
        if (_context is not null) await _context.DisposeAsync();
    }

    [Fact]
    public async Task PrefixedRoot_BootsTheApp()
    {
        await Page.GotoAsync("/sub/");
        await Expect(Page.Locator("h1.display-5"))
            .ToContainTextAsync("The Rask framework",
                new LocatorAssertionsToContainTextOptions { Timeout = 90_000 });
    }

    [Fact]
    public async Task HeadEmits_ScopedCssLinks_UnderPrefix()
    {
        await Page.GotoAsync("/sub/");
        await Expect(Page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 90_000 });

        // Every scoped-CSS <link> must point at the prefixed asset endpoint; none may
        // still use the root-relative path.
        var prefixedHrefs = await Page.Locator("link[rel=stylesheet][href*='/_rask/a/']").CountAsync();
        Assert.True(prefixedHrefs > 0, "expected at least one scoped-CSS <link>");

        var hrefs = await Page.EvalOnSelectorAllAsync<string[]>(
            "link[rel=stylesheet][href*='/_rask/a/']",
            "links => links.map(l => l.getAttribute('href'))");
        Assert.All(hrefs, h => Assert.StartsWith("/sub/_rask/a/", h));
    }

    [Fact]
    public async Task UnprefixedRoot_Returns404()
    {
        // The host moved every framework endpoint under /sub. A direct fetch to the
        // origin root must not be served — confirms endpoints aren't double-mapped.
        var response = await Page.APIRequest.GetAsync(_app.OriginUrl + "/");
        Assert.Equal((int)System.Net.HttpStatusCode.NotFound, response.Status);
    }

    [Fact]
    public async Task PrefixedAssetEndpoint_Returns200()
    {
        // Pick the first hash off the rendered head and fetch it — the same code path
        // the browser uses on first paint. A 200 here is the closed-loop proof that
        // the head emission and the endpoint registration agreed on the prefix.
        await Page.GotoAsync("/sub/");
        await Expect(Page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 90_000 });

        var firstHref = await Page.Locator("link[rel=stylesheet][href*='/_rask/a/']")
            .First.GetAttributeAsync("href");
        Assert.NotNull(firstHref);
        Assert.StartsWith("/sub/_rask/a/", firstHref);

        var url = new Uri(new Uri(_app.OriginUrl), firstHref);
        var response = await Page.APIRequest.GetAsync(url.ToString());
        Assert.Equal((int)System.Net.HttpStatusCode.OK, response.Status);
    }
}
