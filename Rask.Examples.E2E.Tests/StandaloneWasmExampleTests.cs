using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     Smoke tests for the standalone <c>Rask.Example.Wasm</c> example, served by
///     WasmAppHost (the dev launcher used by <c>dotnet run</c>). WasmAppHost has no
///     SPA fallback for unknown paths, so every test starts at <c>/</c> and uses the
///     sidebar to reach the section under test — never <c>Page.GotoAsync("/deep")</c>.
/// </summary>
[Collection(StandaloneWasmExampleCollection.Name)]
public sealed class StandaloneWasmExampleTests : IAsyncLifetime
{
    private readonly StandaloneWasmAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public StandaloneWasmExampleTests(StandaloneWasmAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    public async Task InitializeAsync()
    {
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        _page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private async Task GoHomeAsync()
    {
        await _page.GotoAsync("/index.html");
        await Expect(_page.Locator("h1.display-5"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
    }

    private Task ClickSidebarAsync(string label) =>
        _page.Locator("aside.side-nav button.nav-item-btn:has-text(\"" + label + "\")").ClickAsync();

    [Fact]
    public async Task Home_RendersHero()
    {
        try
        {
            await GoHomeAsync();
            await Expect(_page.Locator("h1.display-5"))
                .ToContainTextAsync("The Rask framework",
                    new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        }
        finally
        {
            await TestArtifacts.DumpAsync(_page, "StandaloneWasm", nameof(Home_RendersHero), _app.ServerLog);
        }
    }

    [Fact]
    public async Task Sidebar_NavigatesToTags()
    {
        try
        {
            await GoHomeAsync();
            await ClickSidebarAsync("Tag factories");
            await Expect(_page).ToHaveURLAsync(new Regex(".*/tags$"),
                new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
            await Expect(_page.Locator("main h1.h2"))
                .ToHaveTextAsync("Tag factories",
                    new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        }
        finally
        {
            await TestArtifacts.DumpAsync(_page, "StandaloneWasm", nameof(Sidebar_NavigatesToTags), _app.ServerLog);
        }
    }

    [Fact]
    public async Task Events_ClickCounter_IncrementsTwice()
    {
        try
        {
            await GoHomeAsync();
            await ClickSidebarAsync("Events");
            await Expect(_page.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

            var clickButton = _page.Locator(".sample-result-body button:has-text('Clicks:')").First;
            await clickButton.ClickAsync();
            await clickButton.ClickAsync();

            await Expect(clickButton).ToContainTextAsync("Clicks: 2",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        }
        finally
        {
            await TestArtifacts.DumpAsync(_page, "StandaloneWasm",
                nameof(Events_ClickCounter_IncrementsTwice), _app.ServerLog);
        }
    }

    [Fact]
    public async Task Http_LoadsPostFromJsonPlaceholder()
    {
        try
        {
            await GoHomeAsync();
            await ClickSidebarAsync("HttpClient + DI");
            await Expect(_page.Locator("main h1.h2"))
                .ToHaveTextAsync("HttpClient + DI",
                    new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

            var article = _page.Locator(".sample-result-body article.card");
            await Expect(article).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            await Expect(article.Locator("h3")).Not.ToBeEmptyAsync();
        }
        finally
        {
            await TestArtifacts.DumpAsync(_page, "StandaloneWasm",
                nameof(Http_LoadsPostFromJsonPlaceholder), _app.ServerLog);
        }
    }

    [Fact]
    public async Task ScopedCss_TwoComponents_RenderDifferentColors()
    {
        try
        {
            await GoHomeAsync();
            await ClickSidebarAsync("Scoped CSS");
            await Expect(_page.Locator("main h1.h2"))
                .ToHaveTextAsync("Scoped CSS",
                    new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

            var boxes = _page.Locator(".sample-result-body .box");
            await Expect(boxes).ToHaveCountAsync(2,
                new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

            var firstBg = await boxes.Nth(0).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
            var secondBg = await boxes.Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
            Assert.NotEqual(firstBg, secondBg);
        }
        finally
        {
            await TestArtifacts.DumpAsync(_page, "StandaloneWasm",
                nameof(ScopedCss_TwoComponents_RenderDifferentColors), _app.ServerLog);
        }
    }
}
