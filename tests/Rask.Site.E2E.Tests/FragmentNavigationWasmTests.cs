using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     A plain link to a <c>#fragment</c> on the WebAssembly host — the PWA page's "On this page" rail: the address keeps
///     the fragment, the section scrolls into view, Back and Forward walk between fragments, and Back from another page
///     returns to the fragment it left (#1310). The Server host's half is <c>FragmentNavigationTests</c> in
///     <c>Rask.Server.E2E.Tests</c>; the runtime both share is <c>rask-fragment.ts</c>.
/// </summary>
public sealed class FragmentNavigationWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Heading = "PWA & device APIs";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    private async Task OpenPwaPageAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{Docs}/pwa/");
        await Expect(Page.Locator("main h1")).ToContainTextAsync(Heading, new() { Timeout = 30_000 });
        await WaitForInteractiveAsync();
    }

    private Task FollowRailAsync(string slug) => Page.Locator($"nav[aria-label='On this page'] a[href='#{slug}']").ClickAsync();

    private static Regex EndsWith(string fragment) => new(Regex.Escape(fragment) + "$");

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public Task A_rail_link_keeps_its_fragment_and_Back_and_Forward_walk_between_sections(int width, int height) => RunAsync(async () =>
    {
        await OpenPwaPageAsync(width, height);

        await FollowRailAsync("bluetooth");
        await Expect(Page).ToHaveURLAsync(EndsWith("/pwa/#bluetooth"));
        await Expect(Page.Locator("h2#bluetooth")).ToBeInViewportAsync();
        await FollowRailAsync("usb");
        await Expect(Page.Locator("h2#usb")).ToBeInViewportAsync();
        await Page.GoBackAsync();

        await Expect(Page).ToHaveURLAsync(EndsWith("/pwa/#bluetooth"));
        await Expect(Page.Locator("h2#bluetooth")).ToBeInViewportAsync();
        await Page.GoForwardAsync();
        await Expect(Page).ToHaveURLAsync(EndsWith("/pwa/#usb"));
        await Expect(Page.Locator("h2#usb")).ToBeInViewportAsync();
    });

    // The fragment's entry is the browser's: a render that answered its popstate by rewriting the entry would have
    // dropped the fragment there, and Back from another page would land on the bare address.
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public Task Back_from_another_page_returns_to_the_fragment_it_left(int width, int height) => RunAsync(async () =>
    {
        await OpenPwaPageAsync(width, height);
        await FollowRailAsync("serial");
        await Expect(Page).ToHaveURLAsync(EndsWith("/pwa/#serial"));
        await Page.EvaluateAsync(
            "() => [...document.querySelectorAll('a[data-rask-nav]')].find(a => /^\\/docs\\/.+/.test(new URL(a.href).pathname) && !new URL(a.href).pathname.includes('pwa') && !a.hash).click()");
        await Expect(Page.Locator("main h1")).Not.ToContainTextAsync(Heading, new() { Timeout = 15_000 });

        await Page.GoBackAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync(Heading, new() { Timeout = 15_000 });

        await Expect(Page).ToHaveURLAsync(EndsWith("/pwa/#serial"));
    });
}
