using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Walks Flux UI's carousel and the kit's with the same pointer and the same keys, and holds every step equal.
/// </summary>
/// <remarks>
///     <c>flux parity carousel</c> compares the page as it loads: every carousel at its start. What a carousel IS only shows
///     once it moves, so this drives each of Flux's eight live carousels and its Rask twin — the static parity page with
///     the runtime's carousel hook added (<see cref="Flux.Runtime" />), or a running site with <c>--live &lt;url&gt;</c> —
///     and writes one line per step: where the track stands, which slide and which indicator are selected, what the root
///     and each arrow say (data-at-start / data-at-end, disabled, how visible the arrow is). The two transcripts must be
///     equal line for line.
///     <para>
///         It waits on STATE, never on a delay: a step is over when the track has stood still for a few frames. Autoplay is
///         the exception — a clock is what it is — and is held to the half second.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages   # writes the Rask page
///     flux parity-carousel
///     flux parity-carousel --live http://localhost:5000/ui/carousel/
///     flux parity-carousel --record                                # Flux alone, every line printed
///     flux parity-carousel --view 390x844                          # on a phone: taps and a swipe
///     </code>
///     On a phone there is no pointer to hover with and no keyboard for a button: a press is a tap, the drag is a swipe (a
///     native scroller scrolls under a finger on both sides), and the hover that holds autoplay is a tap on the slide. The
///     key steps, the tab stops and the focus that holds autoplay are a desktop's and are skipped.
/// </remarks>
[FluxCommand("parity-carousel", "a carousel's walk — arrows, keys, indicators, autoplay — against Flux's")]
public sealed class ParityCarousel : IFluxCommand
{
    // Flux's page, in the order it draws them. The kit's page and the site's demo keep the same order.
    private const int Basic = 0;
    private const int Dots = 1;
    private const int Auto = 2;
    private const int External = 3;
    private const int Overlap = 5;
    private const int Paged = 7;

    // Runs in the page, on a carousel: one line of what it says.
    private const string State = """
        (el, s) => {
          const track = el.querySelector(s.track);
          const slides = [...track.querySelectorAll(s.slide)];
          const dots = [...el.querySelectorAll(`${s.dots} button`)].filter(b => !b.closest('template'));
          const flags = node => (node.hasAttribute('data-at-start') ? 'S' : '-') + (node.hasAttribute('data-at-end') ? 'E' : '-');
          const arrowOf = direction => {
            const name = el.getAttribute('data-name') ?? el.getAttribute('name');
            const wrapper = el.querySelector(s[direction])
              ?? [...document.querySelectorAll(`${s.controls} ${s[direction]}`)].find(w => !name || (w.closest(s.controls).getAttribute('data-name') ?? w.getAttribute('name')) === name);
            if (!wrapper) return 'none';
            const button = wrapper.querySelector('button');
            const style = getComputedStyle(wrapper);
            return `${flags(wrapper)} ${button.disabled ? 'off' : 'on'} opacity=${style.opacity} pointer=${style.pointerEvents}`;
          };
          const max = track.scrollWidth - track.clientWidth;
          return [
            `at=${max ? Math.round(100 * track.scrollLeft / max) : 0}%`,
            `slide=${slides.findIndex(x => x.hasAttribute('data-selected')) + 1}/${slides.length}`,
            `root=${flags(el)}`,
            `prev[${arrowOf('previous')}]`,
            `next[${arrowOf('next')}]`,
            dots.length ? `dot=${dots.findIndex(d => d.hasAttribute('data-selected')) + 1} current=${dots.findIndex(d => d.getAttribute('aria-current') === 'true') + 1}` : 'dots=none',
          ].join(' ');
        }
        """;

    // Runs in the page. Over when the track has stood on one spot for six frames and no transition is running: an arrow
    // that fades may stand outside the carousel, among the external controls. A spinner turns for ever and a
    // scroll-driven fade runs for as long as its track exists; neither is waited for.
    private const string Settle = """
        (el, selector) => new Promise(resolve => {
          const track = el.querySelector(selector);
          let last = track.scrollLeft, still = 0, frames = 0;
          const tick = () => {
            still = track.scrollLeft === last ? still + 1 : 0;
            last = track.scrollLeft;
            if (still >= 6 && document.getAnimations().every(a => a.playState !== 'running' || !(a.timeline instanceof DocumentTimeline) || a.effect?.getComputedTiming().iterations === Infinity)) return resolve();
            if (++frames > 600) return resolve();
            requestAnimationFrame(tick);
          };
          requestAnimationFrame(tick);
          // A page that draws no frame (a headless one left alone) is not waited on for ever.
          setTimeout(resolve, 12000);
        })
        """;

    private const string Selected = "(el, s) => [...el.querySelectorAll(s)].findIndex(x => x.hasAttribute('data-selected')) + 1";

    private sealed record Side(string Url, string Root, string Track, string Slide, Func<string, string> Arrow, string Dots, string Controls, bool Runtime);

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var record = arg.Flag("record");
        var live = arg.Option("live");
        var view = View.Of(arg);

        var flux = new Side("https://fluxui.dev/components/carousel", "[data-flux-carousel]", "[data-flux-carousel-track]", "[data-flux-carousel-slide]",
            direction => $"ui-carousel-button[direction=\"{direction}\"]", "ui-carousel-indicators", "[data-flux-carousel-controls]", false);
        var raskPage = Repo.Parity("rask", "carousel.html");
        var rask = new Side(live ?? Repo.FileUrl(raskPage), "[data-ui-carousel]", "[data-ui-carousel-track]", "[data-ui-carousel-slide]",
            direction => $"[data-direction=\"{direction}\"]", "[data-rask-carousel-indicators]", "[data-ui-carousel-controls]", live is null);
        if (!record && live is null && !File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux carousel: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        var browser = await Browsers.Chromium();
        var walker = new Walker(browser, view, live is not null);
        var theirs = await walker.Walk(flux);
        var mine = record ? theirs : await walker.Walk(rask);
        await browser.CloseAsync();

        var failed = 0;
        var length = Math.Max(theirs.Count, mine.Count);
        for (var i = 0; i < length; i++)
        {
            var a = i < theirs.Count ? theirs[i] : "undefined";
            var b = i < mine.Count ? mine[i] : "undefined";
            var same = a == b;
            failed += same ? 0 : 1;
            if (record || !same)
            {
                Console.WriteLine(same ? $"     {a}" : $"FAIL flux: {a}\n     rask: {b}");
            }
        }

        Console.WriteLine(record
            ? $"flux carousel: {theirs.Count} steps recorded."
            : failed > 0 ? $"flux carousel: {failed} of {theirs.Count} steps differ." : $"flux carousel: all {theirs.Count} steps match Flux.");
        return failed > 0 ? 1 : 0;
    }

    private sealed class Walker(IBrowser browser, View view, bool live)
    {
        private readonly bool phone = view.Phone;

        public async Task<List<string>> Walk(Side side)
        {
            var context = await browser.NewContextAsync(view.Context());
            var page = await context.NewPageAsync();
            await Load(page, side, reload: false, fonts: true);

            var lines = new List<string>();
            ILocator Carousel(int index) => page.Locator(side.Root).Nth(index);

            // The arrows of a carousel are its own, or — with none of its own — the first external controls on the page.
            async Task<ILocator> Arrow(int index, string direction)
            {
                var own = Carousel(index).Locator(side.Arrow(direction)).Locator("button");
                return await own.CountAsync() > 0 ? own.First : page.Locator(side.Controls).Locator(side.Arrow(direction)).Locator("button").First;
            }

            async Task<string> StateOf(int index) => Js.Str(await Carousel(index).Run(State, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["track"] = side.Track,
                ["slide"] = side.Slide,
                ["dots"] = side.Dots,
                ["controls"] = side.Controls,
                ["previous"] = side.Arrow("previous"),
                ["next"] = side.Arrow("next"),
            })) ?? "";

            Task SettleOn(int index) => Carousel(index).Run(Settle, side.Track);

            // Acts, then waits for the track to leave its place: a scroll starts a frame or more after what asked for it,
            // and a track that has not started yet looks exactly like one that has come to rest. One that never leaves
            // (an arrow at its end, a key that is not the carousel's) is given a second and a half to show it.
            async Task Moving(int index, Func<Task> act)
            {
                var from = Js.Number(await Carousel(index).Locator(side.Track).Run("t => t.scrollLeft"));
                await act();
                try
                {
                    await page.WaitForFunctionAsync("([selector, at, left]) => document.querySelectorAll(selector)[at].scrollLeft !== left",
                        new object[] { side.Track, index, from }, new PageWaitForFunctionOptions { Timeout = 1500 });
                }
                catch (TimeoutException)
                {
                    // it never left: an arrow at its end
                }
            }

            async Task Step(int index, string name, Func<Task>? act = null)
            {
                if (act is not null)
                {
                    await act();
                }

                await SettleOn(index);
                lines.Add($"#{index} {name,-34} {await StateOf(index)}");
            }

            // A click, or a tap on a phone.
            Task Hit(ILocator target, float? timeout = null) => phone
                ? target.TapAsync(new LocatorTapOptions { Timeout = timeout })
                : target.ClickAsync(new LocatorClickOptions { Timeout = timeout });

            // An arrow with nowhere to go is disabled, or lets the pointer through: the press is made and nothing follows.
            async Task Press(int index, string direction)
            {
                var button = await Arrow(index, direction);
                if (await button.IsDisabledAsync())
                {
                    return;
                }

                await Moving(index, async () =>
                {
                    try
                    {
                        await Hit(button, 2000);
                    }
                    catch (Exception error) when (error is TimeoutException or PlaywrightException)
                    {
                        // a press the arrow lets through: nothing follows
                    }
                });
            }

            // A phone has no pointer to park.
            Task Park() => phone ? Task.CompletedTask : page.Mouse.MoveAsync(2, 2);

            Task ByHand(int index, int left) => Carousel(index).Locator(side.Track).Run($"t => t.scrollTo({{ left: {(left == 0 ? "0" : "t.scrollWidth")}, behavior: 'instant' }})");

            // The plain carousel: both arrows, each end, the keyboard on an arrow, a scroll by hand and a drag over the track.
            await Carousel(Basic).ScrollIntoViewIfNeededAsync();
            await Park();
            await Step(Basic, "at rest");
            await Step(Basic, "next", () => Press(Basic, "next"));
            await Step(Basic, "next again (the end)", () => Press(Basic, "next"));
            await Step(Basic, "previous", () => Press(Basic, "previous"));
            if (!phone)
            {
                await Step(Basic, "Enter on next", async () =>
                {
                    await (await Arrow(Basic, "next")).FocusAsync();
                    await Moving(Basic, () => page.Keyboard.PressAsync("Enter"));
                });
            }

            await Step(Basic, "previous to the start", async () =>
            {
                await Press(Basic, "previous");
                await SettleOn(Basic);
                await Press(Basic, "previous");
            });
            if (!phone)
            {
                await Step(Basic, "Space on next", async () =>
                {
                    await (await Arrow(Basic, "next")).FocusAsync();
                    await Moving(Basic, () => page.Keyboard.PressAsync("Space"));
                });
                await Step(Basic, "ArrowRight on next", () => page.Keyboard.PressAsync("ArrowRight"));
                await Step(Basic, "ArrowLeft on next", () => page.Keyboard.PressAsync("ArrowLeft"));
                await Step(Basic, "End on next", () => page.Keyboard.PressAsync("End"));
                await Step(Basic, "Home on next", () => page.Keyboard.PressAsync("Home"));
                var focused = Js.Str(await page.Run("() => document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.tagName")) ?? "undefined";
                lines.Add($"#{Basic} focus after the keys              {focused}");
            }

            // A wheel and a swipe are the browser's own scrolling of a native scroller on both sides, and a headless
            // wheel lands differently from one run to the next: the track is scrolled by hand instead, which is what
            // either ends in, and the step holds what follows a scroll nobody pressed a button for.
            await Step(Basic, "scrolled to the end by hand", () => Moving(Basic, () => ByHand(Basic, 1)));
            await Step(Basic, "scrolled to the start by hand", () => Moving(Basic, () => ByHand(Basic, 0)));
            await Step(Basic, phone ? "a swipe across the track" : "a pointer drag across the track", async () =>
            {
                var box = (await Carousel(Basic).Locator(side.Track).BoundingBoxAsync())!;
                var (from, to, y) = ((float)(box.X + (box.Width * 0.8)), (float)(box.X + (box.Width * 0.2)), box.Y + (box.Height / 2));
                if (phone)
                {
                    await Moving(Basic, () => Swipe(page, from, to, y));
                    return;
                }

                await page.Mouse.MoveAsync(from, y);
                await page.Mouse.DownAsync();
                await page.Mouse.MoveAsync(to, y, new MouseMoveOptions { Steps = 8 });
                await page.Mouse.UpAsync();
            });
            if (!phone)
            {
                lines.Add($"#{Basic} tab stops inside                  {await TabStops(page, Carousel(Basic))}");
            }

            // Indicators: each goes to its slide, and follows a move made any other way.
            await Carousel(Dots).ScrollIntoViewIfNeededAsync();
            await Park();
            ILocator Dot(int n) => Carousel(Dots).Locator($"{side.Dots} button").Nth(n);
            await Step(Dots, "at rest");
            await Step(Dots, "third indicator", () => Moving(Dots, () => Hit(Dot(2))));
            await Step(Dots, "first indicator", () => Moving(Dots, () => Hit(Dot(0))));
            if (!phone)
            {
                await Step(Dots, "second indicator by Enter", async () =>
                {
                    await Dot(1).FocusAsync();
                    await Moving(Dots, () => page.Keyboard.PressAsync("Enter"));
                });
            }

            await Step(Dots, "scrolled to the end by hand", () => ByHand(Dots, 1));

            // External controls: they dim instead of leaving, and move the carousel that carries their name.
            await Carousel(External).ScrollIntoViewIfNeededAsync();
            await Park();
            await Step(External, "at rest");
            await Step(External, "external next", () => Press(External, "next"));
            await Step(External, "external next (the end)", () => Press(External, "next"));
            await Step(External, "external previous", () => Press(External, "previous"));

            // Overlap: the same carousel with its arrows half over the edge.
            await Carousel(Overlap).ScrollIntoViewIfNeededAsync();
            await Park();
            await Step(Overlap, "at rest");
            await Step(Overlap, "next", () => Press(Overlap, "next"));
            await Step(Overlap, "previous", () => Press(Overlap, "previous"));

            // Page advance: an arrow moves by the slides in view.
            await Carousel(Paged).ScrollIntoViewIfNeededAsync();
            await Park();
            await Step(Paged, "at rest");
            await Step(Paged, "next page", () => Press(Paged, "next"));
            await Step(Paged, "next page again", () => Press(Paged, "next"));
            await Step(Paged, "next page to the end", async () =>
            {
                await Press(Paged, "next");
                await SettleOn(Paged);
                await Press(Paged, "next");
            });
            await Step(Paged, "previous page", () => Press(Paged, "previous"));

            // Autoplay, on its own clock: from a fresh load, pointer parked, then held under the pointer, then let go.
            await Load(page, side, reload: true, fonts: false);
            await Carousel(Auto).ScrollIntoViewIfNeededAsync();
            await Park();
            async Task<double> SelectedSlide() => Js.Number(await Carousel(Auto).Run(Selected, side.Slide));

            // A move is over when the track rests again: going back to the first slide passes over the second on its way.
            var resting = await SelectedSlide();
            var moved = Environment.TickCount64;
            async Task<string> Move(int ms)
            {
                var started = Environment.TickCount64;
                while (await SelectedSlide() == resting)
                {
                    if (Environment.TickCount64 - started > ms)
                    {
                        return "never";
                    }

                    await page.WaitForTimeoutAsync(40);
                }

                var since = Environment.TickCount64 - moved;
                moved = Environment.TickCount64;
                await SettleOn(Auto);
                resting = await SelectedSlide();
                return $"{Js.Num(Js.Round(since / 500.0) / 2)}s to slide {Js.Num(resting)}";
            }

            await Move(12000);
            lines.Add($"#{Auto} autoplay: the next move            {await Move(12000)}");
            lines.Add($"#{Auto} autoplay: and the one after       {await Move(12000)}");
            lines.Add($"#{Auto} autoplay: and the one after that  {await Move(12000)}");
            // A phone's tap stands in for the hover: the browser makes a mouse that stays where the finger was.
            var track = (await Carousel(Auto).Locator(side.Track).BoundingBoxAsync())!;
            var (cx, cy) = (track.X + (track.Width / 2), track.Y + (track.Height / 2));
            if (phone)
            {
                await Screen.Press(page, cx, cy, phone);
            }
            else
            {
                await page.Mouse.MoveAsync(cx, cy);
            }

            lines.Add($"#{Auto} autoplay: {(phone ? "after a tap on it, 7s " : "under the pointer, 7s")}    {await Move(7000)}");
            if (phone)
            {
                await Screen.Press(page, 2, 2, phone);
            }
            else
            {
                await Park();
            }

            moved = Environment.TickCount64;
            lines.Add($"#{Auto} autoplay: {(phone ? "after a tap outside " : "after the pointer left")}  {await Move(12000)}");
            if (!phone)
            {
                await (await Arrow(Auto, "previous")).FocusAsync();
                lines.Add($"#{Auto} autoplay: focus on an arrow, 7s    {await Move(7000)}");
                await page.Run("() => document.activeElement?.blur()");
                moved = Environment.TickCount64;
                lines.Add($"#{Auto} autoplay: after focus left        {await Move(12000)}");
                // A press on a slide ends it for good, as the tap above does on a phone.
                await page.Mouse.ClickAsync(cx, cy);
                await Park();
                moved = Environment.TickCount64;
                lines.Add($"#{Auto} autoplay: pressed, then left, 7s  {await Move(7000)}");
            }

            await context.CloseAsync();

            var calmOptions = view.Context();
            calmOptions.ReducedMotion = ReducedMotion.Reduce;
            var calm = await browser.NewContextAsync(calmOptions);
            var still = await calm.NewPageAsync();
            await Load(still, side, reload: false, fonts: false);
            await still.Locator(side.Root).Nth(Auto).ScrollIntoViewIfNeededAsync();
            await still.WaitForTimeoutAsync(8000);
            lines.Add($"#{Auto} autoplay: reduced motion, 8s      slide {Js.Num(Js.Number(await still.Locator(side.Root).Nth(Auto).Run(Selected, side.Slide)))}");
            await calm.CloseAsync();
            return lines;
        }

        private async Task Load(IPage page, Side side, bool reload, bool fonts)
        {
            if (reload)
            {
                await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            }
            else
            {
                await page.GotoAsync(side.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            }

            if (side.Runtime)
            {
                await Runtime.With(page, "rask-carousel.ts");
            }

            await page.Locator(side.Root).First.WaitForAsync(new LocatorWaitForOptions { Timeout = live ? 60000 : 15000 });
            if (fonts)
            {
                await page.Run("() => document.fonts.ready.then(() => undefined)");
            }
        }
    }

    // A finger drawn slowly across a track from one x to another, and held still before it lifts, so the browser flings
    // nothing: where the track ends is where its snapping puts it. Playwright's touchscreen only taps.
    private static async Task Swipe(IPage page, double from, double to, double y)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        Task Touch(string type, double? x = null) => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = type,
            ["touchPoints"] = x is null ? Array.Empty<object>() : new object[] { new Dictionary<string, double>(StringComparer.Ordinal) { ["x"] = x.Value, ["y"] = y } },
        });
        await Touch("touchStart", from);
        for (var i = 1; i <= 10; i++)
        {
            await Touch("touchMove", from + ((to - from) * i / 10));
            await page.WaitForTimeoutAsync(16);
        }

        await page.WaitForTimeoutAsync(100);
        await Touch("touchEnd");
        await cdp.DetachAsync();
    }

    // What Tab reaches inside a carousel, in order: each stop by its tag and its name.
    private static async Task<string> TabStops(IPage page, ILocator scope)
    {
        await scope.Run("""
            el => {
              const before = document.createElement('button');
              before.id = 'parity-before';
              el.before(before);
              before.focus();
            }
            """);
        var stops = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var stop = Js.Str(await scope.Run("""
                el => (el.contains(document.activeElement)
                  ? `${document.activeElement.tagName.toLowerCase()}${document.activeElement.getAttribute('aria-label') ? `(${document.activeElement.getAttribute('aria-label')})` : ''}`
                  : null)
                """));
            if (string.IsNullOrEmpty(stop))
            {
                break;
            }

            stops.Add(stop);
        }

        await page.Run("() => document.getElementById('parity-before')?.remove()");
        return stops.Count > 0 ? string.Join(" > ", stops) : "none";
    }
}
