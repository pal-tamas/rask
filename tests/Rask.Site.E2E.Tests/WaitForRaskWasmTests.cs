using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Testing;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>await page.WaitForRask()</c> (Rask.Testing.Playwright) on the WebAssembly host: the boot, a press dispatched
///     into .NET and a navigation are each on the page when the wait returns, read with no retrying assertion. The
///     Server host's half, with held replies and a page that pushes every second, is <c>WaitForRaskTests</c> in
///     <c>Rask.Server.E2E.Tests</c>.
/// </summary>
public sealed class WaitForRaskWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // Clicks the element and says whether <html> was marked busy by the time the click had returned.
    private const string ClickAndRead =
        "s => { document.querySelector(s).click(); return document.documentElement.hasAttribute('data-rask-busy'); }";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_boot_and_a_press_are_on_the_page_when_the_wait_returns() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs + "/guides/routing");
        await Page.WaitForRask();
        var interactive = await Page.Locator("html[data-rask-prerendered]").CountAsync();
        await Expect(Page.Locator("#nav-set-page1")).ToBeAttachedAsync(new() { Timeout = 30_000 });

        var markedAtOnce = await Page.EvaluateAsync<bool>(ClickAndRead, "#nav-set-page1");
        await Page.WaitForRask();

        Assert.Equal(0, interactive);
        Assert.True(markedAtOnce);
        Assert.Equal("page=1", await Page.Locator("#nav-query").TextContentAsync());
    });

    [Fact]
    public Task A_navigation_has_landed_when_the_wait_returns() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs + "/guides/routing");
        await Page.WaitForRask();
        await Expect(Page.Locator("#nav-set-page1")).ToBeAttachedAsync(new() { Timeout = 30_000 });
        var link = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('a[data-rask-nav]')].map(a => a.getAttribute('href'))"
            + ".find(h => h && h.includes('/guides/') && !h.includes('/guides/routing'))");

        var markedAtOnce = await Page.EvaluateAsync<bool>(ClickAndRead, $"a[data-rask-nav][href=\"{link}\"]");
        await Page.WaitForRask();

        Assert.True(markedAtOnce);
        Assert.EndsWith(link.TrimEnd('/'), new Uri(Page.Url).AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
        Assert.Equal(0, await Page.Locator("#nav-set-page1").CountAsync());
    });
}
