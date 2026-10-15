using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     A move of the query alone takes the current history entry's place on the WebAssembly host, through the routing
///     guide's <c>Go.With</c> demo. The Server host's half is <c>QueryNavigationHistoryTests</c> in
///     <c>Rask.Server.E2E.Tests</c>.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class QueryNavigationHistoryWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Go_With_changes_the_address_and_adds_no_history_entry() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs + "/guides/routing");
        await WaitForInteractiveAsync();
        var set = Page.Locator("#nav-set-page1");
        await Expect(set).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await set.ScrollIntoViewIfNeededAsync();
        var before = await Page.EvaluateAsync<int>("() => history.length");

        await set.ClickAsync();
        await Expect(Page.Locator("#nav-query")).ToContainTextAsync("page=1", new() { Timeout = 10_000 });
        await Page.Locator("#nav-set-page2").ClickAsync();
        await Expect(Page.Locator("#nav-query")).ToContainTextAsync("page=2", new() { Timeout = 10_000 });

        Assert.Contains("page=2", Page.Url, StringComparison.Ordinal);
        Assert.Equal(before, await Page.EvaluateAsync<int>("() => history.length"));
    });
}
