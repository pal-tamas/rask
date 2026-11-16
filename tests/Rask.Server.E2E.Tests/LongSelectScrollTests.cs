using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A select of hundreds or thousands of options, scrolled and nothing else, in a real browser over the real socket:
///     the wheel, the scrollbar and the keys reach the last option, it is in view with no blank room under it, every row
///     drawn is exactly as tall as the window counts it, and the last option is the one picked. The lists are TA's and
///     Eszaf's pickers: 1200 vehicles of which a few say a detail under their name and some names are very long; 480
///     activity codes with long Hungarian names and no detail at all (Eszaf's TEÁOR picker); and a pillbox of 600 of
///     those — at a desktop's width and a phone's, where those names would wrap.
/// </summary>
public sealed class LongSelectScrollTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string, string, int> Journeys()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var list in LongSelectScrollPage.Lists)
        {
            foreach (var how in new[] { "wheel", "scrollbar", "end" })
            {
                foreach (var width in new[] { 1280, 390 })
                {
                    data.Add(list, how, width);
                }
            }
        }

        return data;
    }

    public static TheoryData<string, int> Widths()
    {
        var data = new TheoryData<string, int>();
        foreach (var list in LongSelectScrollPage.Lists)
        {
            data.Add(list, 1280);
            data.Add(list, 390);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Journeys))]
    public Task Scrolling_alone_reaches_the_last_option_with_no_blank_room_under_it_and_picks_it(string list, string how, int width) =>
        LongSelectScroll.ReachTheEndAsync<LongSelectScrollPage>(playwright, list, how, width);

    // A page with no reset of its own leaves every box content-box: a row's padding would add to the height it is given.
    [Theory]
    [MemberData(nameof(Widths))]
    public Task On_a_page_with_no_reset_of_its_own_the_keys_reach_the_last_option_all_the_same(string list, int width) =>
        LongSelectScroll.ReachTheEndAsync<BareLongSelectScrollPage>(playwright, list, "end", width);

    [Fact]
    public async Task A_long_name_is_cut_short_on_one_line_at_a_phones_width_and_said_whole_in_its_title()
    {
        await using var session = await HookSession.OpenAsync<LongSelectScrollPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = 390, Height = 844 } });
        await session.HooksLoadedAsync();
        await session.Page.Locator(LongSelectScroll.Opener("codes")).ClickAsync();
        var row = session.Page.Locator(LongSelectScroll.Box("codes") + " [role=option][aria-posinset='1']");

        var shape = await row.EvaluateAsync<string>(
            "row => { const words = row.querySelector('.truncate'); return row.getBoundingClientRect().height + ' ' + (words.scrollWidth > words.clientWidth) + ' ' + getComputedStyle(words).textOverflow; }");

        Assert.Equal("32 true ellipsis", shape);
        Assert.Equal(LongSelectScrollPage.Code(1), await row.GetAttributeAsync("title"));
    }

    // A slow link (#1344): the box is scrolled back to the top and on to the end before the page answers either. The answer
    // for the top comes first and draws the row the cursor was on, far from where the box is now; lit again, that row was
    // named and scrolled to, and the box went back to the top for good.
    [Fact]
    public async Task An_answer_for_where_the_box_was_scrolled_before_leaves_it_where_the_reader_scrolled_it_last()
    {
        var replies = new HeldReplies();
        await using var session = await HookSession.OpenAsync<LongSelectScrollPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = 1280, Height = 844 } }, beforeLoad: replies.RouteAsync);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.Locator(LongSelectScroll.Opener("codes")).ClickAsync();
        var box = page.Locator(LongSelectScroll.Box("codes"));
        var first = box.Locator("[aria-posinset='1']");
        await Expect(first).ToHaveAttributeAsync("data-active", string.Empty);
        await box.EvaluateAsync("box => box.scrollTop = box.scrollHeight / 2");
        await Expect(first).ToHaveCountAsync(0);

        replies.Hold();
        var asked = replies.Scrolls;
        await box.EvaluateAsync("box => box.scrollTop = 0");
        await replies.ScrolledAsync(asked + 1);
        await box.EvaluateAsync("box => box.scrollTop = box.scrollHeight");
        await replies.ScrolledAsync(asked + 2);
        replies.Release();
        var settled = await LongSelectScroll.SettledAsync(box, LongSelectScrollPage.LastOf("codes"));

        Assert.InRange(settled.Gap, 0, LongSelectScroll.Padding);
        Assert.True(await LongSelectScroll.AtTheEndAsync(box), "the box left the end it was scrolled to");
        await Expect(first).ToHaveCountAsync(0);
    }

    /// <summary>The page's socket, with what the server sends held back while asked and then let through in order.</summary>
    private sealed class HeldReplies
    {
        private readonly Lock _gate = new();

        private readonly Queue<string> _held = new();

        private IWebSocketRoute? _page;

        private bool _holding;

        private int _scrolls;

        /// <summary>How many scroll frames the page has sent.</summary>
        internal int Scrolls => Volatile.Read(ref _scrolls);

        internal Task RouteAsync(IPage page) =>
            page.RouteWebSocketAsync(new System.Text.RegularExpressions.Regex("/rask/ws"), socket =>
            {
                _page = socket;
                var server = socket.ConnectToServer();
                socket.OnMessage(frame =>
                {
                    if (frame.Text is { } text && text.Contains("\"type\":\"scroll\"", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref _scrolls);
                    }

                    server.Send(frame.Text!);
                });
                server.OnMessage(frame =>
                {
                    lock (_gate)
                    {
                        if (_holding)
                        {
                            _held.Enqueue(frame.Text!);
                            return;
                        }

                        socket.Send(frame.Text!);
                    }
                });
            });

        internal void Hold()
        {
            lock (_gate)
            {
                _holding = true;
            }
        }

        internal void Release()
        {
            lock (_gate)
            {
                _holding = false;
                while (_held.TryDequeue(out var text))
                {
                    _page!.Send(text);
                }
            }
        }

        /// <summary>Waits until the page has sent <paramref name="count" /> scroll frames in all.</summary>
        internal async Task ScrolledAsync(int count)
        {
            for (var i = 0; i < 200 && Scrolls < count; i++)
            {
                await Task.Delay(25);
            }

            Assert.True(Scrolls >= count, "the page sent no scroll frame");
        }
    }
}

/// <summary>Scrolls a windowed list to its end the ways a reader does, and measures what is under its last row.</summary>
internal static class LongSelectScroll
{
    /// <summary>The list's own padding under its last row: Flux's .3125rem, and a pixel for rounding.</summary>
    internal const int Padding = 6;

    internal static string Box(string list) => "#" + list + "-at [data-rask-list-window]";

    internal static string Opener(string list) => list == "tags" ? "#tags-at [data-ui-pillbox-trigger]" : "#" + list;

    internal static async Task ReachTheEndAsync<TPage>(PlaywrightFixture playwright, string list, string how, int width)
        where TPage : LongSelectScrollPage, new()
    {
        // A browser that shows its scrollbars, so there is a thumb to drag where the platform draws one.
        await using var browser = await playwright.Playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var session = await HookSession.OpenAsync<TPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = 844 } }, browser: browser);
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.Locator(Opener(list)).ClickAsync();
        var box = page.Locator(Box(list));
        await Expect(box).ToBeVisibleAsync();

        await ToTheEndAsync(page, box, how);
        var settled = await SettledAsync(box, LongSelectScrollPage.LastOf(list));
        await box.Locator("[aria-posinset='" + LongSelectScrollPage.CountOf(list).ToString(CultureInfo.InvariantCulture) + "']").ClickAsync();

        Assert.InRange(settled.Gap, 0, Padding);
        Assert.True(settled.Under >= 0, "the last option is cut off at the top of the list");
        Assert.Equal(settled.Counted.ToString(CultureInfo.InvariantCulture), settled.Heights);
        await Expect(page.Locator("#picked")).ToHaveTextAsync(list + ":" + LongSelectScrollPage.CountOf(list).ToString(CultureInfo.InvariantCulture));
    }

    private static async Task ToTheEndAsync(IPage page, ILocator box, string how)
    {
        switch (how)
        {
            case "wheel":
                await box.HoverAsync();
                for (var i = 0; i < 400 && !await AtTheEndAsync(box); i++)
                {
                    await page.Mouse.WheelAsync(0, 1500);
                    await page.WaitForTimeoutAsync(30);
                }

                break;
            case "scrollbar":
                await DragTheThumbAsync(page, box);
                break;
            default:
                // The keys of an open list: End lands on the last row, and the box scrolls to it.
                await page.Keyboard.PressAsync("End");
                break;
        }
    }

    // The thumb is dragged past the very bottom where the platform draws a scrollbar that takes room; where it draws them
    // over the content (macOS), the box is put where such a drag leaves it: at its end.
    private static async Task DragTheThumbAsync(IPage page, ILocator box)
    {
        var bar = await box.EvaluateAsync<int>("box => box.offsetWidth - box.clientWidth");
        var rect = await box.BoundingBoxAsync();
        if (bar <= 0 || rect is null)
        {
            await box.EvaluateAsync("box => box.scrollTop = box.scrollHeight");
            return;
        }

        // Where the thumb starts is the platform's (an arrow button above it or none): found by pressing down the top of the
        // bar and pulling a little. A press on the track pages the box by at most its height; one on the thumb carries it
        // dozens of rows for each pixel pulled. The box goes back to its top after each try.
        var x = rect.X + rect.Width - ((float)bar / 2) - 1;
        var grab = rect.Y + 4;
        for (var offset = 4; offset <= 40; offset += 3)
        {
            await box.EvaluateAsync("box => box.scrollTop = 0");
            await page.Mouse.MoveAsync(x, rect.Y + offset);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(x, rect.Y + offset + 20, new() { Steps = 4 });
            await page.Mouse.UpAsync();
            if (await box.EvaluateAsync<bool>("box => box.scrollTop > box.clientHeight * 2"))
            {
                grab = rect.Y + offset;
                break;
            }
        }

        await box.EvaluateAsync("box => box.scrollTop = 0");
        await page.Mouse.MoveAsync(x, grab);
        await page.Mouse.DownAsync();
        for (var step = 1; step <= 10; step++)
        {
            await page.Mouse.MoveAsync(x, grab + (step * (rect.Height + 200) / 10));
        }

        await page.Mouse.UpAsync();
        // A headless browser does not always hand its thumb to a synthetic press (measured: in some runs no press along the
        // bar moved the box at all). What is under test is the box at the very bottom of its bar, so a drag that did not
        // take is finished where it would have left the box.
        if (!await AtTheEndAsync(box))
        {
            await box.EvaluateAsync("box => box.scrollTop = box.scrollHeight");
        }
    }

    internal static Task<bool> AtTheEndAsync(ILocator box) =>
        box.EvaluateAsync<bool>("box => box.scrollTop + box.clientHeight >= box.scrollHeight - 1");

    /// <summary>
    ///     Once the page has drawn the rows the box is scrolled to: the blank room between the bottom of the row named
    ///     <paramref name="last" /> and the bottom of the box (negative when the row is cut off there, a large number when
    ///     it is not drawn at all), how far under the box's top that row starts, every height the rows drawn have, and the
    ///     height the window counts each row at.
    /// </summary>
    internal static async Task<Settled> SettledAsync(ILocator box, string last)
    {
        var said = string.Empty;
        var same = 0;
        for (var i = 0; i < 100 && same < 5; i++)
        {
            var now = await box.EvaluateAsync<string>(
                """
                (box, last) => {
                    const rows = [...box.querySelectorAll('[role=option],[data-ui-listbox-option]')];
                    const heights = [...new Set(rows.map(r => Math.round(r.getBoundingClientRect().height)))].join(',');
                    const counted = box.getAttribute('data-rask-list-window').split(' ')[0];
                    const row = rows.find(r => r.getAttribute('title') === last);
                    if (!row) return ['999999', '0', heights, counted].join('|');
                    const top = box.getBoundingClientRect().top + box.clientTop, at = row.getBoundingClientRect();
                    return [Math.round(top + box.clientHeight - at.bottom), Math.round(at.top - top), heights, counted].join('|');
                }
                """,
                last);
            same = string.Equals(now, said, StringComparison.Ordinal) ? same + 1 : 0;
            said = now;
            await box.Page.WaitForTimeoutAsync(60);
        }

        var parts = said.Split('|');
        return new Settled(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            parts[2],
            int.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    internal readonly record struct Settled(int Gap, int Under, string Heights, int Counted);
}

/// <summary>
///     The long lists: TA's vehicles, a few with a detail and some with a very long name; Eszaf's activity codes; and a
///     pillbox of those codes. The kit's own reset stands in for an app's Tailwind preflight: border-box, its line heights.
/// </summary>
public partial class LongSelectScrollPage : Component
{
    internal static readonly string[] Lists = ["fleet", "codes", "tags"];

    private const int Vehicles = 1200;

    private const int Codes = 480;

    private const int Tags = 600;

    private static readonly string[] Activities =
    [
        "Közúti áruszállítás",
        "Személygépjármű, könnyűgépjármű kereskedelme, javítása és karbantartása, valamint alkatrész-kiskereskedelem",
        "Egyéb szárazföldi személyszállítás, máshova nem sorolt, beleértve a menetrend szerinti távolsági autóbusz-közlekedést",
        "Raktározás, tárolás",
        "Szállítást, szállítmányozást kiegészítő egyéb szolgáltatás, rakománykezelés és a logisztikai szolgáltatás egyéb formái",
    ];

    private string _picked = "none";

    private ICollection<int> _tags = [];

    internal static string LastOf(string list) => list switch
    {
        "fleet" => Vehicle(Vehicles),
        "codes" => Code(Codes),
        _ => Code(Tags),
    };

    internal static int CountOf(string list) => list switch
    {
        "fleet" => Vehicles,
        "codes" => Codes,
        _ => Tags,
    };

    // Every 50th name is very long, the last one among them; every 7th vehicle says a detail under its name, the rest none.
    private static string Vehicle(int id) =>
        "Vehicle " + id.ToString("0000", CultureInfo.InvariantCulture)
                   + (id % 50 == 0 ? " — articulated refrigerated semi-trailer with a tail lift and a second cooling unit" : string.Empty);

    private static string? Detail(int id) => id % 7 == 0 ? "Trailer, " + (id * 3 % 40).ToString(CultureInfo.InvariantCulture) + " t" : null;

    // "4001 Személygépjármű, …": three names in five are 80 to 120 characters long, the first and the last among them.
    internal static string Code(int id) =>
        (4000 + id).ToString(CultureInfo.InvariantCulture) + " " + Activities[(id + 1) % Activities.Length];

    protected override Component? HeadAssets => [Markup.Title["long select scroll"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "hu";

    protected virtual bool Reset => true;

    protected override Component? Render() => Main.Class("p-4 space-y-4").Attributes(Reset ? [(UiStylesheet.DocumentAttribute, "")] : [])[
        Div.Id("fleet-at")[
            Ui.Select.Value(Picked("fleet")).OnChange(value => _picked = "fleet:" + value?.ToString(CultureInfo.InvariantCulture)).Listbox.Searchable()
                .Placeholder("Choose a vehicle...").Id("fleet")[
                    Enumerable.Range(1, Vehicles).Select(id => Ui.SelectOption.Key(id).Value(id).Description(Detail(id))[Vehicle(id)])
                ]
        ],
        Div.Id("codes-at")[
            Ui.Select.Value(Picked("codes")).OnChange(value => _picked = "codes:" + value?.ToString(CultureInfo.InvariantCulture)).Listbox
                .Placeholder("Válassz tevékenységet...").Id("codes")[
                    Enumerable.Range(1, Codes).Select(id => Ui.SelectOption.Key(id).Value(id)[Code(id)])
                ]
        ],
        Div.Id("tags-at")[
            Ui.Pillbox.Values(_tags).OnChange(values => (_tags, _picked) = (values, "tags:" + string.Join(",", values))).Placeholder("Tevékenységek...")[
                Enumerable.Range(1, Tags).Select(id => Ui.PillboxOption.Key(id).Value(id)[Code(id)])
            ]
        ],
        P.Id("picked")[_picked]
    ];

    private int? Picked(string list) =>
        _picked.StartsWith(list + ":", StringComparison.Ordinal) ? int.Parse(_picked[(list.Length + 1)..], CultureInfo.InvariantCulture) : null;
}

/// <summary>The same lists on a page with no reset at all: every box content-box, as the browser leaves it.</summary>
public sealed partial class BareLongSelectScrollPage : LongSelectScrollPage
{
    protected override bool Reset => false;
}
