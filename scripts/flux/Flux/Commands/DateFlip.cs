using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Which side an OPEN date picker keeps when its calendar changes height.
/// </summary>
/// <remarks>
///     A month of six weeks is a row taller than one of five. A picker low in the viewport opens its calendar under the
///     trigger on a five-week month; paged to a six-week one, the calendar no longer fits there. This opens Flux's live
///     picker with the trigger's bottom at the same place as the app that reported it (scrolled there, as a form page
///     is), pages month by month and writes down where the calendar is after each step, then what makes it choose again:
///     a scroll, a resize of the window, closing and opening it.
///     <code>flux date-flip [--rask &lt;url&gt; [--picker &lt;css&gt;]] [--view 1440x950] [--bottom 616]</code>
///     <c>--rask</c> measures a running Rask page the same way (its first <c>[data-ui-date-picker]</c>, or
///     <c>--picker</c>) beside Flux's button picker, and compares the side of every step: each must be Flux's. Exits 1 on
///     a difference.
/// </remarks>
[FluxCommand("date-flip", "which side an open date picker keeps as its month changes height")]
public sealed partial class DateFlip : IFluxCommand
{
    private sealed record Side(string Name, string Url, string Picker, int Index);

    // Runs in the page. Scrolls so the picker's trigger ends `bottom` px down the viewport.
    private const string Scroll = """
        ({ picker, index, bottom }) => {
          const el = document.querySelectorAll(picker)[index];
          window.scrollBy(0, el.getBoundingClientRect().bottom - bottom);
        }
        """;

    // Runs in the page. A picker that holds a value opens on its month: emptied, it opens on today.
    private const string Clear = """
        ({ picker, index }) => {
          const el = document.querySelectorAll(picker)[index];
          if (!el.hasAttribute('data-empty') && 'value' in el) el.value = '';
        }
        """;

    // Runs in the page. Where the calendar is, which month it shows and how many weeks that month has.
    private const string Read = """
        ({ picker, index }) => {
          const el = document.querySelectorAll(picker)[index];
          const t = el.getBoundingClientRect();
          const panel = document.querySelector('dialog[open]') ?? document.querySelector('[popover]:popover-open');
          const round = v => Math.round(v);
          if (!panel) return { open: false, month: '', weeks: 0, trigger: `${round(t.top)}..${round(t.bottom)}` };
          const p = panel.getBoundingClientRect();
          // The month is the one the day in the middle of the grid belongs to: "Thursday, October 15, 2026".
          const days = [...panel.querySelectorAll('[aria-label]')].map(d => d.getAttribute('aria-label')).filter(l => /\d{4}$/.test(l));
          const middle = (days[Math.floor(days.length / 2)] ?? '').split(', ');
          const rows = [...panel.querySelectorAll('tbody tr, [role=row]')].filter(r => r.querySelector('td, [role=gridcell]'));
          return {
            open: true,
            month: middle.length > 2 ? `${middle[1].split(' ')[0]} ${middle[2]}` : '',
            weeks: rows.length,
            side: p.top >= t.bottom - 1 ? 'below' : p.bottom <= t.top + 1 ? 'above' : 'over',
            top: round(p.top),
            bottom: round(p.bottom),
            trigger: `${round(t.top)}..${round(t.bottom)}`,
          };
        }
        """;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var views = arg.Option("view") is { } v ? [v.Split('x').Select(int.Parse).ToArray()] : new[] { new[] { 1440, 950 }, [1400, 900] };
        var bottom = double.Parse(arg.Option("bottom") ?? "616", CultureInfo.InvariantCulture);

        // The first picker of the page whose trigger is a button, opened on no value, and the typed one.
        var fluxButton = new Side("flux button", "https://fluxui.dev/components/date-picker", "[data-preview-wrapper] [data-flux-date-picker]", 9);
        var sides = arg.Option("rask") is { } rask
            ? new[] { fluxButton, new Side("rask", rask, arg.Option("picker") ?? "[data-ui-date-picker]", 0) }
            : [fluxButton, new Side("flux input", "https://fluxui.dev/components/date-picker", "[data-preview-wrapper] [data-flux-date-picker]", 1)];
        var sidesOf = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);

        var browser = await Browsers.Chromium();
        foreach (var side in sides)
        {
            foreach (var view in views)
            {
                var (width, height) = (view[0], view[1]);
                var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height } });
                // October 2026 has five weeks, September five, August six, July five.
                await context.Clock.SetFixedTimeAsync("2026-10-21T12:00:00Z");
                var page = await context.NewPageAsync();
                await page.GotoAsync(side.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
                await page.Run("() => document.fonts.ready.then(() => undefined)");
                Console.WriteLine($"\n{side.Name} at {width}x{height}, the trigger's bottom at {Js.Num(bottom)}");
                var rows = sidesOf[$"{side.Name} {width}x{height}"] = [];
                var where = new { picker = side.Picker, index = side.Index };
                async Task Note(string step)
                {
                    var at = (JsonObject)(await page.Run(Read, where))!;
                    at["step"] = step;
                    rows.Add(at);
                    var open = Js.Truthy(at["open"]);
                    Console.WriteLine($"  {step,-34} {Js.Str(at["month"]),-16} {Js.Num(Js.Number(at["weeks"]))} weeks  trigger {Js.Str(at["trigger"])}  popup {(open ? $"{Js.Str(at["side"])} top {Js.Num(Js.Number(at["top"]))} bottom {Js.Num(Js.Number(at["bottom"]))}" : "closed")}");
                }

                var scroll = new { picker = side.Picker, index = side.Index, bottom };
                await page.Run(Scroll, scroll);
                await Opener(page, side).ClickAsync();
                await Settle(page);
                await page.Run(Clear, where);
                await Settle(page);
                await page.Run(Scroll, scroll);
                await Settle(page);
                await Note("opened");
                foreach (var step in new[] { "previous", "previous", "previous", "next", "next", "next" })
                {
                    await Pager(page, step).ClickAsync();
                    await Settle(page);
                    await Note($"{step} month");
                }

                // Back on a six-week month: what makes it choose again.
                foreach (var step in new[] { "previous", "previous" })
                {
                    await Pager(page, step).ClickAsync();
                    await Settle(page);
                }

                await Note("previous ×2 (back to six weeks)");
                await page.Run("() => window.scrollBy(0, 1)");
                await Settle(page);
                await Note("scrolled the page by 1 px");
                await page.Run("() => window.scrollBy(0, -1)");
                await Settle(page);
                await Note("scrolled back");
                await page.SetViewportSizeAsync(width, height + 1);
                await Settle(page);
                await page.SetViewportSizeAsync(width, height);
                await Settle(page);
                await Note("window resized by 1 px and back");
                await page.Keyboard.PressAsync("Escape");
                await Settle(page);
                await Opener(page, side).ClickAsync();
                await Settle(page);
                await Note("closed and opened again");
                await context.CloseAsync();
            }
        }

        await browser.CloseAsync();
        if (arg.Option("rask") is null)
        {
            return 0;
        }

        var failed = 0;
        Console.WriteLine("\nrask against flux");
        foreach (var view in views)
        {
            var (width, height) = (view[0], view[1]);
            var flux = sidesOf[$"flux button {width}x{height}"];
            var mine = sidesOf[$"rask {width}x{height}"];
            for (var i = 0; i < flux.Count; i++)
            {
                var theirs = flux[i];
                var ours = i < mine.Count ? mine[i] : null;
                var ok = Js.Str(ours?["side"]) == Js.Str(theirs["side"]);
                failed += ok ? 0 : 1;
                var tag = ok ? "ok" : "FAIL";
                Console.WriteLine($"  {tag,-8} {width}x{height} {Js.Str(theirs["step"]),-34} flux {Js.Str(theirs["side"]) ?? "closed"}, rask {Js.Str(ours?["side"]) ?? "closed"}");
            }
        }

        return failed > 0 ? 1 : 0;
    }

    // The button that opens it: the trigger itself, or the one beside a typed field (Flux's is the chevron).
    private static ILocator Opener(IPage page, Side side) =>
        page.Locator(side.Picker).Nth(side.Index)
            .Locator("button[role=combobox], ui-date-picker-trigger button, ui-date-picker-trigger > svg:last-child, [data-ui-date-picker-trigger] button").First;

    private static ILocator Pager(IPage page, string step) =>
        page.Locator("dialog[open], [popover]:popover-open").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { NameRegex = step == "previous" ? Previous() : Next() }).First;

    private static Task Settle(IPage page) => page.Run("""
        () => new Promise(done => {
          let n = 0;
          const tick = () => (++n >= 12 ? done() : requestAnimationFrame(tick));
          requestAnimationFrame(tick);
        })
        """);

    [GeneratedRegex("previous", RegexOptions.IgnoreCase)]
    private static partial Regex Previous();

    [GeneratedRegex("next", RegexOptions.IgnoreCase)]
    private static partial Regex Next();
}
