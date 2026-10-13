using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Carousel</c> and an unbound <c>Ui.Toggle</c> on a live SERVER page: a gesture that only changes
///     what the component draws is the browser's, and sends the server nothing — as Flux's does.
/// </summary>
/// <remarks>
///     The socket is watched in both directions while the carousel is moved by its arrows, its indicators, its
///     controls elsewhere on the page and its own autoplay, and while a toggle nobody listens to is turned by a
///     press and by the keyboard. Then the page is rendered again by something else, and what the reader did
///     must still stand.
/// </remarks>
public sealed class UiDrawnInTheBrowserTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Selected =
        "id => [...document.querySelectorAll('#' + id + ' [data-ui-carousel-slide]')].findIndex(s => s.hasAttribute('data-selected')) + 1";

    [Fact]
    public async Task A_carousel_moved_by_arrows_indicators_outside_controls_and_autoplay_sends_the_server_nothing()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#plain [data-direction=next] button");
        await SlideAsync(page, "plain", 2);
        await page.ClickAsync("#plain [data-rask-carousel-indicators] button >> nth=2");
        await SlideAsync(page, "plain", 3);
        await page.ClickAsync("#plain [data-direction=previous] button");
        await SlideAsync(page, "plain", 2);
        await page.ClickAsync("[data-ui-carousel-controls] [data-direction=next] button");
        await SlideAsync(page, "named", 2);
        await SlideAsync(page, "auto", 2, 4_000);

        // Three presses, one indicator, one press outside the carousel and one move of its own: not one frame.
        Assert.Equal((0, 0), watched.Frames());
        await Expect(page.Locator("#plain")).Not.ToHaveAttributeAsync("data-at-start", "");
        await Expect(page.Locator("#plain [data-direction=previous] button")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task A_toggle_nobody_listens_to_is_turned_by_a_press_Space_and_Enter_and_sends_the_server_nothing()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        var toggle = page.Locator("#own");

        await toggle.ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(toggle).ToHaveAttributeAsync("data-checked", "");
        await page.Keyboard.PressAsync("Space");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "false");
        await page.Keyboard.PressAsync("Enter");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "true");
        await page.Locator("#off").ClickAsync(new LocatorClickOptions { Force = true });

        // Three turns, and a press on a disabled one: not one frame, and the disabled one did not turn.
        Assert.Equal((0, 0), watched.Frames());
        await Expect(page.Locator("#off")).ToHaveAttributeAsync("aria-checked", "false");
        await Expect(toggle.Locator("svg[fill='currentColor']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task What_the_reader_did_in_the_browser_stands_when_the_page_is_rendered_again()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#own");
        await page.ClickAsync("#plain [data-direction=next] button");
        await SlideAsync(page, "plain", 2);

        await page.ClickAsync("#tick");

        await Expect(page.Locator("#tick")).ToHaveTextAsync("Ticked 1");
        await Expect(page.Locator("#own")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(page.Locator("#own")).ToHaveAttributeAsync("data-checked", "");
        Assert.Equal(2, await page.EvaluateAsync<int>(Selected, "plain"));
        await Expect(page.Locator("#plain [data-direction=previous] button")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task A_toggle_the_page_listens_to_still_tells_it_and_is_drawn_from_what_the_page_says()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#heard");

        await Expect(page.Locator("#says")).ToHaveTextAsync("on");
        await Expect(page.Locator("#heard")).ToHaveAttributeAsync("aria-checked", "true");
        Assert.NotEqual((0, 0), watched.Frames());
    }

    private static Task SlideAsync(IPage page, string id, int place, int timeout = 3_000) =>
        page.WaitForFunctionAsync(
            "([id, place]) => [...document.querySelectorAll('#' + id + ' [data-ui-carousel-slide]')].findIndex(s => s.hasAttribute('data-selected')) + 1 === place",
            new object[] { id, place },
            new() { Timeout = timeout });

    // A session whose socket is counted from the moment the page and its hooks are in.
    private sealed class Watched(HookSession session, int[] counts) : IAsyncDisposable
    {
        private readonly int _sentBefore = Volatile.Read(ref counts[0]);
        private readonly int _receivedBefore = Volatile.Read(ref counts[1]);

        public HookSession Session => session;

        public static async Task<Watched> OpenAsync(PlaywrightFixture playwright)
        {
            var counts = new int[2];
            var session = await HookSession.OpenAsync<DrawnInTheBrowserPage>(playwright, beforeLoad: page =>
            {
                page.WebSocket += (_, socket) =>
                {
                    socket.FrameSent += (_, _) => Interlocked.Increment(ref counts[0]);
                    socket.FrameReceived += (_, _) => Interlocked.Increment(ref counts[1]);
                };
                return Task.CompletedTask;
            });
            await session.HooksLoadedAsync();
            // Whatever the load itself exchanged has gone by: from here on the socket is watched.
            await session.Page.WaitForTimeoutAsync(300);
            return new Watched(session, counts);
        }

        /// <summary>Frames sent and received since the page settled.</summary>
        public (int Sent, int Received) Frames() =>
            (Volatile.Read(ref counts[0]) - _sentBefore, Volatile.Read(ref counts[1]) - _receivedBefore);

        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}

/// <summary>Three carousels and three toggles, drawn by the kit on a Server page.</summary>
public sealed partial class DrawnInTheBrowserPage : Component
{
    private int _ticks;
    private bool _heard;

    protected override Component? HeadAssets => [Markup.Title["drawn in the browser"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("width:320px;margin:16px")[
            Button.Id("tick").OnClick(() => _ticks++)[$"Ticked {_ticks}"],
            Div.Id("plain")[Ui.Carousel.Key("plain").Indicators().Instant[Slides("plain")]],
            Ui.CarouselControls.Name("stays"),
            Div.Id("named")[Ui.Carousel.Key("named").Name("stays").Arrows(false).Instant[Slides("named")]],
            Div.Id("auto")[Ui.Carousel.Key("auto").Autoplay().AutoplayInterval(700).Instant[Slides("auto")]],
            Ui.Toggle.Key("own").Id("own").Icon(Ui.IconName.Bolt),
            Ui.Toggle.Key("off").Id("off").Icon(Ui.IconName.Bolt).Disabled(),
            Ui.Toggle.Key("heard").Id("heard").Icon(Ui.IconName.Bell).Checked(_heard).OnChange(on => { _heard = on; }),
            Span.Id("says")[_heard ? "on" : "off"]
        ];

    private static IEnumerable<Component> Slides(string carousel) =>
        new[] { "one", "two", "three" }.Select(name =>
            Ui.CarouselSlide.Key(carousel + "-" + name).Class("w-full")[Div.Style("height:60px")[name]]);
}
