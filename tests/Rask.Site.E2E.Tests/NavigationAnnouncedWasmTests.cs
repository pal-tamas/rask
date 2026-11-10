using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     A navigation on the WebAssembly host, for a reader who cannot see it arrive — as on the Server host: focus moves
///     into the new page's <c>&lt;main&gt;</c>, its title is announced in the polite live region, and Back announces
///     the page it returns to. The Server host's half is <c>NavigationAnnouncedTests</c> in <c>Rask.Server.E2E.Tests</c>;
///     the runtime both share is <c>rask-route-focus.ts</c>.
/// </summary>
public sealed class NavigationAnnouncedWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Region = ".rask-route-announcer";

    // The page's own <main> — the first in document order, the one the runtime picks.
    private const string MainFocused = "() => document.activeElement === document.querySelector('main, [role=main]')";

    private const string SaysTitle = "() => document.querySelector('.rask-route-announcer').textContent === document.title && document.title !== ''";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    // A reader's click on the link to a UI page, wherever the layout shows it: on a phone the sidebar is folded away.
    private Task ClickLinkToAsync(string slug) =>
        Page.EvaluateAsync(
            "s => [...document.querySelectorAll('a[href]')].find(a => new RegExp('/ui/' + s + '/?$').test(new URL(a.href).pathname)).click()",
            slug);

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public Task A_link_focuses_the_new_pages_main_content_and_announces_its_title(int width, int height) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenUiPageAsync("form-tabs", "Form tabs");
        await Page.FocusAsync("#uft-save");

        await ClickLinkToAsync("modal");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Modal", new() { Timeout = 15_000 });
        await Page.WaitForFunctionAsync(SaysTitle);

        Assert.True(await Page.EvaluateAsync<bool>(MainFocused), "focus did not move into the new page's main content");
        await Expect(Page.Locator(Region)).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(Page.Locator(Region)).ToContainTextAsync("Modal");
        Assert.Equal(0, await Page.Locator(".rask-pending[data-show]").CountAsync());
    });

    [Fact]
    public Task Back_and_Forward_announce_the_page_they_arrive_at() => RunAsync(async () =>
    {
        await OpenUiPageAsync("form-tabs", "Form tabs");
        await ClickLinkToAsync("modal");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Modal", new() { Timeout = 15_000 });
        await Page.WaitForFunctionAsync(SaysTitle);

        await Page.GoBackAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Form tabs", new() { Timeout = 15_000 });
        await Page.WaitForFunctionAsync(SaysTitle);
        var back = await Page.Locator(Region).TextContentAsync();
        await Page.GoForwardAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Modal", new() { Timeout = 15_000 });
        await Page.WaitForFunctionAsync(SaysTitle);

        Assert.Contains("Form tabs", back, StringComparison.Ordinal);
        await Expect(Page.Locator(Region)).ToContainTextAsync("Modal");
    });
}
