using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     What a measured virtual list (<c>Virtualize.Items</c> drawn between <c>ctx.Above</c> and <c>ctx.Below</c>) does in
///     a real browser, whichever host draws it: it opens at its <c>InitialItemIndex</c>, <c>ScrollTo</c> lands a row at
///     the top, rows of mixed heights leave no bare box wherever it is scrolled and reach both ends, a list anchored to
///     its end follows its tail until scrolled up, and a table's rows are held by the runtime. Each row says its index in
///     <c>data-row</c>.
/// </summary>
/// <remarks>Shared by the Server host's suite, the browser host's, and the site's log demo, so all three are driven alike.</remarks>
internal static class VirtualListJourneys
{
    // How far a row may stand from where it should: a fractional scrollTop rounds.
    private const double Slack = 2;

    // Where `box` stands: its spacer's window, scrollTop and spacer heights, the first row in view and how far it is
    // from the top, the row ends the box shows, how much bare spacer it shows, and whether it is at its end.
    private const string Read = """
        (sel) => {
            const box = document.querySelector(sel);
            const above = [...box.querySelectorAll('[data-rask-virtual]')][0];
            let below = above.nextElementSibling;
            while (below && !below.hasAttribute('data-rask-virtual-below')) below = below.nextElementSibling;
            const r = box.getBoundingClientRect();
            const top = r.top + box.clientTop, bottom = top + box.clientHeight;
            const a = above.getBoundingClientRect(), b = below.getBoundingClientRect();
            const bare = Math.max(0, Math.min(a.bottom, bottom) - Math.max(a.top, top))
                + Math.max(0, Math.min(b.bottom, bottom) - Math.max(b.top, top));
            let first = -1, at = 0;
            for (const row of box.querySelectorAll('[data-row]')) {
                const rr = row.getBoundingClientRect();
                if (rr.bottom > top + 0.5) { first = Number(row.dataset.row); at = rr.top - top; break; }
            }
            return {
                stamp: above.getAttribute('data-rask-virtual') + '|' + above.style.height + '|' + below.style.height + '|' + Math.round(box.scrollTop),
                first, at, bare,
                end: box.scrollHeight - box.clientHeight - box.scrollTop < 2,
                belowHeight: b.height,
            };
        }
        """;

    private const string RowTop = """
        ([sel, row]) => {
            const box = document.querySelector(sel);
            const el = box.querySelector('[data-row="' + row + '"]');
            if (!el) return null;
            const r = box.getBoundingClientRect();
            const top = r.top + box.clientTop;
            const e = el.getBoundingClientRect();
            return { top: e.top - top, bottom: e.bottom - top, height: box.clientHeight };
        }
        """;

    /// <summary>The list opens with row <paramref name="row" /> at the top of its box.</summary>
    public static async Task OpensAtAsync(IPage page, string box, int row)
    {
        await SettledAsync(page, box);

        var at = await RowAsync(page, box, row);

        Assert.NotNull(at);
        Assert.InRange(at.Top, -Slack, Slack);
    }

    /// <summary>Whether the list, once settled, stands at the end of its box.</summary>
    public static async Task<bool> SettlesAtTheEndAsync(IPage page, string box) => (await SettledAsync(page, box)).End;

    /// <summary>A press of <paramref name="button" /> scrolls row <paramref name="row" /> to the top, and the page hears it landed there.</summary>
    public static async Task ScrollsToAsync(IPage page, string box, string button, string landed, int row)
    {
        await SettledAsync(page, box);

        await page.Locator(button).ClickAsync();
        await Expect(page.Locator(landed)).ToHaveTextAsync("landed:" + row);
        var arrived = await RowAsync(page, box, row);
        await SettledAsync(page, box);
        var stays = await RowAsync(page, box, row);

        Assert.NotNull(arrived);
        Assert.InRange(arrived.Top, -Slack, Slack);
        Assert.NotNull(stays);
        Assert.InRange(stays.Top, -Slack, Slack);
    }

    /// <summary>
    ///     Scrolled anywhere — by script, by the wheel, to either end — the box shows rows and no bare spacer once it has
    ///     settled, the rows in view stay put, and the first and the last of the <paramref name="rows" /> are reached.
    /// </summary>
    public static async Task ScrollsTrueAsync(IPage page, string box, int rows)
    {
        foreach (var fraction in new[] { 0.5, 0.1, 0.9, 0.3 })
        {
            await page.EvaluateAsync("([sel, f]) => { const b = document.querySelector(sel); b.scrollTop = f * (b.scrollHeight - b.clientHeight); }", new object[] { box, fraction });
            await CoveredAndStillAsync(page, box);
        }

        await page.Locator(box).HoverAsync();
        for (var i = 0; i < 12; i++)
        {
            await page.Mouse.WheelAsync(0, 240);
            await page.WaitForTimeoutAsync(30);
        }

        await CoveredAndStillAsync(page, box);

        var end = await ReachAsync(page, box, "b => b.scrollTop = b.scrollHeight", rows - 1, atEnd: true);
        var start = await ReachAsync(page, box, "b => b.scrollTop = 0", 0, atEnd: false);

        Assert.True(end, "the last row was not reached");
        Assert.True(start, "the first row was not reached");
    }

    /// <summary>
    ///     An end-anchored log opens at its tail, follows the rows <paramref name="append" /> adds while at the bottom,
    ///     and stays where the reader scrolled it once they scroll up.
    /// </summary>
    public static async Task FollowsTheTailAsync(IPage page, string box, string append, string count, int first, int added)
    {
        var opened = await SettledAsync(page, box);
        var openedLast = await RowAsync(page, box, first - 1);
        await page.Locator(append).ClickAsync();
        await Expect(page.Locator(count)).ToHaveTextAsync((first + added).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var followed = await SettledAsync(page, box);
        var followedLast = await RowAsync(page, box, first + added - 1);

        await page.Locator(box).HoverAsync();
        await page.Mouse.WheelAsync(0, -600);
        var away = await SettledAsync(page, box);
        await page.Locator(append).ClickAsync();
        await Expect(page.Locator(count)).ToHaveTextAsync((first + (2 * added)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var stayed = await SettledAsync(page, box);
        var unseen = await RowAsync(page, box, first + (2 * added) - 1);

        Assert.True(opened.End, "the log did not open at its tail");
        Assert.NotNull(openedLast);
        Assert.True(openedLast.Bottom <= openedLast.Height + Slack, "the last row is below the box");
        Assert.True(followed.End, "the log did not follow the rows added at its tail");
        Assert.NotNull(followedLast);
        Assert.True(followedLast.Bottom <= followedLast.Height + Slack, "the newest row is below the box");
        Assert.False(away.End, "the wheel did not scroll the log up");
        Assert.False(stayed.End, "the log followed its tail after the reader scrolled up");
        Assert.Equal(away.First, stayed.First);
        Assert.InRange(stayed.At - away.At, -Slack, Slack);
        Assert.True(unseen is null || unseen.Top >= unseen.Height, "the newest row came into view after the reader scrolled up");
    }

    /// <summary>A table's rows: the runtime holds them itself (the box anchors nothing), and they scroll true.</summary>
    public static async Task HoldsATableAsync(IPage page, string box, int rows)
    {
        await SettledAsync(page, box);
        var anchoring = await page.EvaluateAsync<string>("sel => getComputedStyle(document.querySelector(sel)).overflowAnchor", box);

        await ScrollsTrueAsync(page, box, rows);

        Assert.Equal("none", anchoring);
    }

    private static async Task CoveredAndStillAsync(IPage page, string box)
    {
        var settled = await SettledAsync(page, box);
        await page.WaitForTimeoutAsync(400);
        var later = await StateAsync(page, box);

        Assert.True(settled.Bare <= 1, $"the box shows {settled.Bare}px of bare spacer at {settled.Stamp}");
        Assert.Equal(settled.First, later.First);
        Assert.InRange(later.At - settled.At, -Slack, Slack);
    }

    // Scrolls with `act` until the row is in view against the edge it is at, as a reader would: a few times at most.
    private static async Task<bool> ReachAsync(IPage page, string box, string act, int row, bool atEnd)
    {
        for (var i = 0; i < 10; i++)
        {
            await page.Locator(box).EvaluateAsync(act);
            var state = await SettledAsync(page, box);
            var at = await RowAsync(page, box, row);
            if (at is not null && state.Bare <= 1
                && (atEnd ? state.End && at.Bottom <= at.Height + Slack && state.BelowHeight < 1 : Math.Abs(at.Top) <= Slack))
            {
                return true;
            }
        }

        return false;
    }

    // Waits until the list stops moving: its window, spacers and scroll the same over three looks in a row.
    private static async Task<State> SettledAsync(IPage page, string box)
    {
        var last = await StateAsync(page, box);
        var same = 0;
        for (var i = 0; i < 100 && same < 3; i++)
        {
            await page.WaitForTimeoutAsync(120);
            var now = await StateAsync(page, box);
            same = string.Equals(now.Stamp, last.Stamp, StringComparison.Ordinal) ? same + 1 : 0;
            last = now;
        }

        Assert.True(same >= 3, $"the list never settled: {last.Stamp}");
        return last;
    }

    private static async Task<State> StateAsync(IPage page, string box)
    {
        var s = await page.EvaluateAsync<JsonElement>(Read, box);
        return new State(
            s.GetProperty("stamp").GetString()!, s.GetProperty("first").GetInt32(), s.GetProperty("at").GetDouble(),
            s.GetProperty("bare").GetDouble(), s.GetProperty("end").GetBoolean(), s.GetProperty("belowHeight").GetDouble());
    }

    private static async Task<Row?> RowAsync(IPage page, string box, int row)
    {
        var r = await page.EvaluateAsync<JsonElement?>(RowTop, new object[] { box, row });
        return r is not { ValueKind: JsonValueKind.Object } at
            ? null
            : new Row(at.GetProperty("top").GetDouble(), at.GetProperty("bottom").GetDouble(), at.GetProperty("height").GetDouble());
    }

    internal sealed record State(string Stamp, int First, double At, double Bare, bool End, double BelowHeight);

    internal sealed record Row(double Top, double Bottom, double Height);
}
