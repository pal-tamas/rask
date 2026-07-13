using System.Net;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The sub-path host's single journey. Boots Rask.Example.Wasm.Host with
///     <c>pathBase: "/sub"</c> backed by a Rask.Example.Wasm AppBundle published with
///     <c>/p:RaskPathBase=/sub</c>, and verifies the whole prefix contract end to end: the app
///     boots under the prefix, every scoped-asset URL honors it, the prefixed asset endpoint
///     serves 200, and the unprefixed origin root 404s (endpoints moved, not duplicated). The
///     back-end endpoint routing is unit-tested in Rask.Wasm.Hosting.Tests.
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
        // BaseURL is the origin (no prefix). The journey passes "/sub/..." explicitly so
        // Playwright's absolute-path resolution doesn't drop the prefix.
        _context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.OriginUrl });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_page is not null)
        {
            try { await TestArtifacts.DumpAsync(_page, "SubPathWasm", "after-test", _app.ServerLog); }
            catch
            {
                /* best effort */
            }

            await _page.CloseAsync();
        }

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }

    [Fact]
    public async Task Journey_BootsUnderPrefix_AssetsAndEndpointsHonorPrefix()
    {
        // 1. The app boots under the /sub prefix. Guides-first: "/" is the guides index (the Welcome
        // landing page is gone), whose PageHeader renders an <h1 class="h2">Guides</h1>.
        await Page.GotoAsync("/sub/");
        await Expect(Page.Locator("main h1.h2"))
            .ToContainTextAsync("Guides",
                new LocatorAssertionsToContainTextOptions { Timeout = 90_000 });

        // 2. Every scoped-CSS <link> points at the prefixed asset endpoint; none stays root-relative.
        var linkCount = await Page.Locator("link[rel=stylesheet][href*='/_rask/a/']").CountAsync();
        Assert.True(linkCount > 0, "expected at least one scoped-CSS <link>");
        var hrefs = await Page.EvalOnSelectorAllAsync<string[]>(
            "link[rel=stylesheet][href*='/_rask/a/']",
            "links => links.map(l => l.getAttribute('href'))");
        Assert.All(hrefs, h => Assert.StartsWith("/sub/_rask/a/", h));

        // 3. The prefixed asset endpoint serves 200 — the closed-loop proof that head emission and
        //    endpoint registration agreed on the prefix (same path the browser fetches on first paint).
        var firstHref = await Page.Locator("link[rel=stylesheet][href*='/_rask/a/']")
            .First.GetAttributeAsync("href");
        Assert.NotNull(firstHref);
        var assetUrl = new Uri(new Uri(_app.OriginUrl), firstHref!);
        var assetResponse = await Page.APIRequest.GetAsync(assetUrl.ToString());
        Assert.Equal((int)HttpStatusCode.OK, assetResponse.Status);

        // 4. The unprefixed origin root must 404 — every framework endpoint moved under /sub.
        var rootResponse = await Page.APIRequest.GetAsync(_app.OriginUrl + "/");
        Assert.Equal((int)HttpStatusCode.NotFound, rootResponse.Status);
    }
}
