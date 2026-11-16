using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A plain link to a <c>#fragment</c> of the page is the browser's own move: the address keeps the fragment, the
///     anchor scrolls into view, Back and Forward walk between fragments, and nothing is asked of the server (#1310).
/// </summary>
/// <remarks>
///     The runtime half both hosts share is <c>rask-fragment.ts</c>; the WebAssembly host's journey is the site's
///     <c>FragmentNavigationWasmTests</c>.
/// </remarks>
public sealed class FragmentNavigationTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Navigates = "() => window.__kept.filter(frame => frame.includes('\"navigate\"')).length";

    private async Task<HookSession> OpenAsync(int width, int height)
    {
        var session = await HookSession.OpenAsync<FragmentPage>(
            playwright, new() { ViewportSize = new() { Width = width, Height = height } }, SentFrames.Keep, path: "/long");
        await session.Page.WaitForFunctionAsync("() => document.querySelector('[data-rask-root]') !== null && window.__kept.length > 0");
        return session;
    }

    // Two frames on: whatever the click's popstate would have sent has left the page by now.
    private static Task SettledAsync(IPage page) =>
        page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_link_to_a_fragment_keeps_it_in_the_address_and_scrolls_to_its_anchor(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;

        await page.ClickAsync("#to-far");
        await SettledAsync(page);

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/long#far$"));
        await Expect(page.Locator("#far")).ToBeInViewportAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>(Navigates));
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Back_and_Forward_walk_between_fragments_without_asking_the_server(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;
        await page.ClickAsync("#to-far");
        await page.ClickAsync("#to-farther");
        await Expect(page.Locator("#farther")).ToBeInViewportAsync();

        await page.GoBackAsync();
        await SettledAsync(page);
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/long#far$"));
        await Expect(page.Locator("#far")).ToBeInViewportAsync();
        await page.GoBackAsync();
        await SettledAsync(page);
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/long$"));
        await page.GoForwardAsync();
        await SettledAsync(page);

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/long#far$"));
        await Expect(page.Locator("#far")).ToBeInViewportAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>(Navigates));
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Back_from_another_page_returns_to_the_fragment_it_left(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;
        await page.ClickAsync("#to-far");
        await page.ClickAsync("#to-other");
        await Expect(page.Locator("#where")).ToHaveTextAsync("path=/other");

        await page.GoBackAsync();
        await Expect(page.Locator("#where")).ToHaveTextAsync("path=/long");

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/long#far$"));
    }
}

/// <summary>A page taller than the screen, with links to two anchors far down it and a link to another path.</summary>
public sealed partial class FragmentPage(RouteState route) : Component
{
    protected override Component? HeadAssets => Markup.Title["fragments"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("where")[$"path={route.Path}"],
        A.Href("#far").Id("to-far")["far"],
        NavLink.Href("/other").Id("to-other")["other"],
        Div.Style("height: 3000px")["top"],
        H2.Id("far")["Far"],
        A.Href("#farther").Id("to-farther")["farther"],
        Div.Style("height: 3000px")["middle"],
        H2.Id("farther")["Farther"],
        Div.Style("height: 3000px")["bottom"]
    ];
}
