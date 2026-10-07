using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A carousel's position, reported to everything around its track, in a real browser.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live carousel page on 2026-10-07: the root and both arrow wrappers carry
///     <c>data-at-start</c> at rest and "previous" is disabled; the first slide has <c>data-selected</c>; the
///     autoplay example (interval 3500 ms) did not move for 7 s under the pointer and moved 3.5 s after it left,
///     rewound from the end, and with <c>prefers-reduced-motion: reduce</c> did not move in 16 s.
/// </remarks>
public sealed class RuntimeHookCarouselTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string State =
        "id => { const r = document.getElementById(id); const t = r.querySelector('[data-rask-carousel-track]'); "
        + "return Math.round(t.scrollLeft) + ' ' + (r.hasAttribute('data-at-start') ? 'S' : '-') + (r.hasAttribute('data-at-end') ? 'E' : '-') "
        + "+ ' slide' + [...t.children].findIndex(s => s.hasAttribute('data-selected')) "
        + "+ ' prev=' + (r.querySelector('[data-direction=previous] button').disabled ? 'off' : 'on') "
        + "+ ' next=' + (r.querySelector('[data-direction=next] button').disabled ? 'off' : 'on'); }";

    [Fact]
    public async Task The_arrows_move_a_slide_at_a_time_and_the_ends_disable_the_one_with_nowhere_to_go()
    {
        await using var session = await HookSession.OpenAsync<CarouselHookPage>(playwright);
        var page = session.Page;

        var rest = await page.EvaluateAsync<string>(State, "plain");
        await page.ClickAsync("#plain [data-direction=next] button");
        await Settle(page, "plain", 200);
        var second = await page.EvaluateAsync<string>(State, "plain");
        await page.ClickAsync("#plain [data-direction=next] button");
        await Settle(page, "plain", 400);
        var last = await page.EvaluateAsync<string>(State, "plain");
        await page.ClickAsync("#plain [data-direction=previous] button");
        await Settle(page, "plain", 200);

        Assert.Equal("0 S- slide0 prev=off next=on", rest);
        Assert.Equal("200 -- slide1 prev=on next=on", second);
        Assert.Equal("400 -E slide2 prev=on next=off", last);
        Assert.Equal("200 -- slide1 prev=on next=on", await page.EvaluateAsync<string>(State, "plain"));
    }

    [Fact]
    public async Task A_scroll_the_reader_makes_moves_the_selection_and_the_indicator_with_it()
    {
        await using var session = await HookSession.OpenAsync<CarouselHookPage>(playwright);
        var page = session.Page;

        await page.EvaluateAsync("() => document.querySelector('#plain [data-rask-carousel-track]').scrollTo({ left: 400, behavior: 'instant' })");
        await Settle(page, "plain", 400);
        var dots = await page.EvaluateAsync<string>("() => [...document.querySelectorAll('#plain [data-rask-carousel-indicators] button')].map(b => (b.hasAttribute('data-selected') ? '*' : '.') + (b.getAttribute('aria-current') || '')).join(' ')");
        await page.ClickAsync("#plain [data-rask-carousel-indicators] button >> nth=0");
        await Settle(page, "plain", 0);

        Assert.Equal(". . *true", dots);
        Assert.Equal("0 S- slide0 prev=off next=on", await page.EvaluateAsync<string>(State, "plain"));
    }

    [Fact]
    public async Task Advancing_by_page_moves_by_the_slides_in_view_and_rewinding_goes_back_to_the_start()
    {
        await using var session = await HookSession.OpenAsync<CarouselHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#paged-next button");
        await Settle(page, "paged", 200);
        var afterOnePage = await page.EvaluateAsync<string>(State, "paged");
        await page.ClickAsync("#paged-next button");
        await Settle(page, "paged", 400);
        var atEnd = await page.EvaluateAsync<string>(State, "paged");
        await page.ClickAsync("#paged-next button");
        await Settle(page, "paged", 0);

        // Two 100px slides fit the 200px track; the controls live outside the root, paired by name.
        Assert.Equal("200 -- slide2 prev=on next=on", afterOnePage);
        Assert.Equal("400 -E slide4 prev=on next=on", atEnd);
        Assert.Equal("0 S- slide0 prev=off next=on", await page.EvaluateAsync<string>(State, "paged"));
    }

    [Fact]
    public async Task Autoplay_advances_on_its_interval_and_the_pointer_over_it_stops_it()
    {
        await using var session = await HookSession.OpenAsync<CarouselHookPage>(playwright);
        var page = session.Page;

        await Settle(page, "auto", 200, 3_000);
        await page.HoverAsync("#auto");
        await page.WaitForTimeoutAsync(1_500);
        var held = await page.EvaluateAsync<int>("() => Math.round(document.querySelector('#auto [data-rask-carousel-track]').scrollLeft)");
        await page.Mouse.MoveAsync(900, 650);

        Assert.Equal(200, held);
        await Settle(page, "auto", 400, 3_000);
        await Settle(page, "auto", 0, 3_000);
    }

    [Fact]
    public async Task Autoplay_never_starts_for_a_reader_who_asked_for_less_motion()
    {
        await using var session = await HookSession.OpenAsync<CarouselHookPage>(playwright, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = session.Page;

        await page.WaitForTimeoutAsync(1_500);

        Assert.Equal(0, await page.EvaluateAsync<int>("() => Math.round(document.querySelector('#auto [data-rask-carousel-track]').scrollLeft)"));
    }

    [Fact]
    public async Task A_carousel_rendered_disabled_stays_disabled_and_does_not_move()
    {
        await using var session = await HookSession.OpenAsync<CarouselHookPage>(playwright);
        var page = session.Page;

        await page.EvaluateAsync("() => document.querySelector('#off [data-direction=next] button').click()");
        await page.EvaluateAsync("() => document.querySelector('#off [data-rask-carousel-track]').scrollTo({ left: 200, behavior: 'instant' })");
        await Settle(page, "off", 200);

        Assert.Equal("200 -- slide1 prev=off next=off", await page.EvaluateAsync<string>(State, "off"));
    }

    // Waits for the track to come to rest at `left` and for the frame that reports it.
    private static async Task Settle(IPage page, string id, int left, int timeout = 2_000)
    {
        await page.WaitForFunctionAsync(
            "([id, left]) => Math.abs(document.querySelector('#' + id + ' [data-rask-carousel-track]').scrollLeft - left) < 1",
            new object[] { id, left },
            new() { Timeout = timeout });
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }
}

/// <summary>Four carousels, written out by hand: plain, paged with outside controls, autoplaying, disabled.</summary>
public sealed partial class CarouselHookPage : Component
{
    private const string Html = """
        <style>
          body { margin: 0 }
          [data-rask-carousel] { width: 200px; margin: 10px }
          [data-rask-carousel-track] { display: flex; overflow-x: auto; scroll-snap-type: x mandatory; width: 200px }
          [data-rask-carousel-track] > div { flex: none; width: 200px; height: 40px; scroll-snap-align: start }
          #paged [data-rask-carousel-track] > div { width: 100px }
        </style>
        <div id="plain" data-rask-carousel data-at-start>
          <div data-rask-carousel-track><div data-selected>1</div><div>2</div><div>3</div></div>
          <span data-direction="previous" data-at-start><button type="button" disabled>‹</button></span>
          <span data-direction="next" data-at-start><button type="button">›</button></span>
          <div data-rask-carousel-indicators><button type="button" data-selected aria-current="true">1</button><button type="button">2</button><button type="button">3</button></div>
        </div>
        <div id="paged" data-rask-carousel data-name="stays" data-advance="page" data-wrap="rewind" data-scroll="instant" data-at-start>
          <div data-rask-carousel-track><div>1</div><div>2</div><div>3</div><div>4</div><div>5</div><div>6</div></div>
          <span data-direction="previous"><button type="button" disabled>‹</button></span>
          <span data-direction="next"><button type="button">›</button></span>
        </div>
        <div data-rask-carousel-controls data-name="stays"><span id="paged-next" data-direction="next"><button type="button">›</button></span></div>
        <div id="auto" data-rask-carousel data-autoplay="700" data-scroll="instant">
          <div data-rask-carousel-track><div>1</div><div>2</div><div>3</div></div>
        </div>
        <div id="off" data-rask-carousel>
          <div data-rask-carousel-track><div>1</div><div>2</div><div>3</div></div>
          <span data-direction="previous"><button type="button" disabled>‹</button></span>
          <span data-direction="next"><button type="button" disabled>›</button></span>
        </div>
        """;

    protected override Component? HeadAssets => Markup.Title["carousel hook"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[Raw.Value(Html)];
}
