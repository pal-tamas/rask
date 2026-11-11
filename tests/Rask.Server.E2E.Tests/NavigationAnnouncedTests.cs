using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A navigation on a live server page, for a reader who cannot see it arrive: focus moves into the new page, its
///     title is announced in the polite live region — Back and Forward included — and the top bar shows, in the app's
///     accent, for an answer slower than 150 ms.
/// </summary>
/// <remarks>
///     The runtime halves are <c>rask-route-focus.ts</c> and <c>rask-progress.ts</c> (RouteFocusTests and
///     ProgressTests on Jint); the WASM host's journey is the site's <c>NavigationAnnouncedWasmTests</c>.
/// </remarks>
public sealed class NavigationAnnouncedTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Announced = "() => document.querySelector('.rask-route-announcer').textContent";

    private async Task<HookSession> OpenAsync(string path, int width = 1280, int height = 800, ReducedMotion? motion = null)
    {
        var session = await HookSession.OpenAsync<NavigationAnnouncedApp>(
            playwright, new() { ViewportSize = new() { Width = width, Height = height }, ReducedMotion = motion }, path: path);
        await session.Page.WaitForFunctionAsync("() => document.querySelector('.rask-route-announcer') !== null");
        return session;
    }

    private static Task SaysAsync(IPage page, string title) =>
        page.WaitForFunctionAsync($"t => ({Announced})() === t", title);

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_link_focuses_the_new_pages_main_content_and_announces_its_title_politely(int width, int height)
    {
        await using var session = await OpenAsync("/heard/one", width, height);
        var page = session.Page;

        await page.ClickAsync("#to-two");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Two");
        await SaysAsync(page, "Two | Heard");

        Assert.Equal("main", await session.FocusAsync());
        await Expect(page.Locator(".rask-route-announcer")).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(page.Locator(".rask-route-announcer")).ToHaveAttributeAsync("aria-atomic", "true");
    }

    [Fact]
    public async Task Back_and_Forward_announce_the_page_they_arrive_at()
    {
        await using var session = await OpenAsync("/heard/one");
        var page = session.Page;
        await page.ClickAsync("#to-two");
        await SaysAsync(page, "Two | Heard");

        await page.GoBackAsync();
        await Expect(page.Locator("#heading")).ToHaveTextAsync("One");
        await SaysAsync(page, "One | Heard");
        await page.GoForwardAsync();
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Two");

        await SaysAsync(page, "Two | Heard");
    }

    [Fact]
    public async Task A_link_to_a_fragment_focuses_and_shows_the_element_it_names()
    {
        await using var session = await OpenAsync("/heard/one");
        var page = session.Page;

        await page.ClickAsync("#to-part");
        await Expect(page.Locator("#heading")).ToHaveTextAsync("Two");
        await SaysAsync(page, "Two | Heard");

        await Expect(page.Locator("#part")).ToBeFocusedAsync();
        await Expect(page.Locator("#part")).ToBeInViewportAsync();
    }

    [Fact]
    public async Task A_link_to_the_page_the_reader_is_on_focuses_its_main_content_again()
    {
        await using var session = await OpenAsync("/heard/one");
        var page = session.Page;
        await page.FocusAsync("#press");

        await page.ClickAsync("#to-one");
        await page.WaitForFunctionAsync("() => document.activeElement.id === 'main'");

        Assert.Equal("main", await session.FocusAsync());
    }

    [Fact]
    public async Task A_page_with_neither_main_content_nor_a_heading_is_announced_and_takes_no_focus()
    {
        await using var session = await OpenAsync("/heard/one");
        var page = session.Page;

        await page.ClickAsync("#to-bare");
        await Expect(page.Locator("#bare")).ToBeVisibleAsync();
        await SaysAsync(page, "Bare | Heard");

        Assert.Equal("BODY", await session.FocusAsync());
    }

    [Fact]
    public async Task A_slow_handler_shows_the_bar_in_the_apps_accent_until_it_is_answered()
    {
        await using var session = await OpenAsync("/heard/one");
        var page = session.Page;
        await page.EvaluateAsync("() => document.documentElement.style.setProperty('--color-accent', 'rgb(1, 2, 3)')");

        await page.ClickAsync("#slow-press");
        await Expect(page.Locator(".rask-pending[data-show]")).ToBeVisibleAsync();
        var drawn = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.rask-pending__bar')).backgroundImage");
        await Expect(page.Locator("#pressed")).ToHaveTextAsync("pressed=1", new() { Timeout = 10_000 });

        await Expect(page.Locator(".rask-pending[data-show]")).ToHaveCountAsync(0);
        Assert.Contains("rgb(1, 2, 3)", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slow_handler_shows_the_bar_in_Rasks_purple_when_the_app_has_no_accent()
    {
        await using var session = await OpenAsync("/heard/one");
        var page = session.Page;

        await page.ClickAsync("#slow-press");
        await Expect(page.Locator(".rask-pending[data-show]")).ToBeVisibleAsync();
        var drawn = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.rask-pending__bar')).backgroundImage");
        await Expect(page.Locator("#pressed")).ToHaveTextAsync("pressed=1", new() { Timeout = 10_000 });

        await Expect(page.Locator(".rask-pending[data-show]")).ToHaveCountAsync(0);
        Assert.Contains("rgb(124, 58, 237)", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reader_who_asked_for_less_motion_sees_a_still_bar()
    {
        await using var session = await OpenAsync("/heard/one", motion: ReducedMotion.Reduce);
        var page = session.Page;

        await page.ClickAsync("#slow-press");
        await Expect(page.Locator(".rask-pending[data-show]")).ToBeVisibleAsync();
        var animation = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.rask-pending__bar')).animationName");

        Assert.Equal("none", animation);
    }
}

/// <summary>A small site: a layout with a <c>&lt;main&gt;</c>, two pages, and one with neither main nor heading.</summary>
public sealed partial class NavigationAnnouncedApp : Component
{
    protected override Component? HeadAssets => Markup.Title["Heard"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

[Route("/heard")]
public sealed partial class HeardLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | Heard" : "Heard"];

    protected override Component? Render() => Div[Header[P["Heard"]], Main.Id("main")[Outlet]];
}

[Route("one")]
[ParentRoute(typeof(HeardLayout))]
public sealed partial class HeardOnePage : Component
{
    private int _pressed;

    protected override string? PageTitle => "One";

    protected override Component? Render() =>
    [
        H1.Id("heading")["One"],
        Nav[
            NavLink.Href(Routes.HeardOnePage()).Id("to-one")["One"],
            NavLink.Href(Routes.HeardTwoPage()).Id("to-two")["Two"],
            NavLink.Href("/heard/two#part").Id("to-part")["Two, its part"],
            NavLink.Href(Routes.HeardBarePage()).Id("to-bare")["Bare"]
        ],
        Button.Id("press")["press"],
        Button.Id("slow-press").OnClick(PressSlowly)["press slowly"],
        P.Id("pressed")[$"pressed={_pressed}"]
    ];

    private async Task PressSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        _pressed++;
    }
}

[Route("two")]
[ParentRoute(typeof(HeardLayout))]
public sealed partial class HeardTwoPage : Component
{
    protected override string? PageTitle => "Two";

    protected override Component? Render() =>
    [
        H1.Id("heading")["Two"],
        Div.Style("height:2400px"),
        Section.Id("part")[P["The part a link names."]]
    ];
}

// Outside the layout: no <main>, no <h1>.
[Route("/heard-bare")]
public sealed partial class HeardBarePage : Component
{
    protected override string? PageTitle => "Bare";

    protected override Component? HeadAssets => Title["Bare | Heard"];

    protected override Component? Render() => P.Id("bare")["Nothing to land on."];
}
