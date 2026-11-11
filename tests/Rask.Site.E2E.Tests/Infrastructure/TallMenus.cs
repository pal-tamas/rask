using System.Text.Json;
using Microsoft.Playwright;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     A row's ⋯ menu taller than the viewport, read in a real browser: where its box lands, which of its rows are in
///     that box, and where focus goes as the arrow keys walk it. Shared by the Server suite and the WASM site's, so both
///     hosts are held to one reading.
/// </summary>
internal static class TallMenus
{
    /// <summary>The trigger's row at the top of the viewport.</summary>
    public const string Top = "top";

    /// <summary>The trigger's row in the middle of the viewport.</summary>
    public const string Middle = "middle";

    /// <summary>The trigger's row at the bottom of the viewport.</summary>
    public const string Bottom = "bottom";

    /// <summary>The menu's rows: View, Edit, ten and four in two named groups, one alone, and the delete.</summary>
    public const int Rows = 18;

    // The open menu, its box inside its border, and whether a row is in that box — half a pixel either way.
    private const string Reader = """
        const menu = () => document.querySelector('[role=menu]:popover-open');
        const inner = m => { const b = m.getBoundingClientRect(); const top = b.top + m.clientTop; return { top, bottom: top + m.clientHeight }; };
        const inside = (m, row) => { const box = inner(m); const r = row.getBoundingClientRect(); return r.top >= box.top - 0.5 && r.bottom <= box.bottom + 0.5; };
        const rows = m => [...m.querySelectorAll('[role=menuitem]')];
        """;

    /// <summary>
    ///     Scrolls the page so the row <paramref name="where" /> names sits at the top, the middle or the bottom of the
    ///     viewport, and hands back its ⋯.
    /// </summary>
    public static async Task<ILocator> TriggerAsync(ILocator rows, string where)
    {
        var count = await rows.CountAsync();
        var row = where switch
        {
            Top => rows.First,
            Middle => rows.Nth(count / 2),
            _ => rows.Last,
        };
        var block = where switch
        {
            Top => "start",
            Middle => "center",
            _ => "end",
        };
        // Clear of a header the page holds at its top: the row a reader sees first under it.
        await row.EvaluateAsync("(row, block) => { row.scrollIntoView({ block }); if (block === 'start') scrollBy(0, -80); }", block);
        return row.Locator("[data-ui-row-actions] [popovertarget]");
    }

    /// <summary>The menu once it is open and has stayed in one box for three frames.</summary>
    public static async Task<Opened> OpenedAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.querySelector('[role=menu]:popover-open')?.hasAttribute('data-rask-placed')");
        await page.WaitForFunctionAsync("""
            () => new Promise(done => {
                const box = () => JSON.stringify(document.querySelector('[role=menu]:popover-open').getBoundingClientRect());
                const seen = [];
                const frame = () => { seen.push(box()); if (seen.length < 3) requestAnimationFrame(frame); else done(seen.every(b => b === seen[0])); };
                requestAnimationFrame(frame);
            })
            """, null, new() { PollingInterval = 50 });
        var json = await page.EvaluateAsync<JsonElement>("() => {" + Reader + """
                const m = menu(); const b = m.getBoundingClientRect(); const first = rows(m)[0];
                return { x: b.x, y: b.y, w: b.width, h: b.height,
                    vw: document.documentElement.clientWidth, vh: document.documentElement.clientHeight,
                    scrollTop: m.scrollTop, capped: m.scrollHeight > m.clientHeight,
                    firstInside: inside(m, first), firstFocused: document.activeElement === first };
            }
            """);
        return new Opened(
            json.GetProperty("x").GetDouble(), json.GetProperty("y").GetDouble(), json.GetProperty("w").GetDouble(), json.GetProperty("h").GetDouble(),
            json.GetProperty("vw").GetDouble(), json.GetProperty("vh").GetDouble(), json.GetProperty("scrollTop").GetDouble(),
            json.GetProperty("capped").GetBoolean(), json.GetProperty("firstInside").GetBoolean(), json.GetProperty("firstFocused").GetBoolean());
    }

    /// <summary>
    ///     Scrolls the open menu — the menu alone, as a finger or a wheel over it does — just far enough to bring each row
    ///     into its box in turn, first to last, and says of each whether it is then in the box.
    /// </summary>
    public static async Task<IReadOnlyList<bool>> ScrolledThroughAsync(IPage page) =>
        await page.EvaluateAsync<bool[]>("() => {" + Reader + """
                const m = menu();
                return rows(m).map(row => {
                    const box = inner(m); const r = row.getBoundingClientRect();
                    if (r.bottom > box.bottom) m.scrollTop += r.bottom - box.bottom;
                    else if (r.top < box.top) m.scrollTop -= box.top - r.top;
                    return inside(m, row);
                });
            }
            """);

    /// <summary>
    ///     ArrowDown to the last row and ArrowUp back to the first, from the first: after each key (and before the first),
    ///     the row that has focus and whether it is in the menu's box.
    /// </summary>
    public static async Task<IReadOnlyList<Step>> WalkedAsync(IPage page)
    {
        var steps = new List<Step> { await FocusedAsync(page) };
        foreach (var key in Enumerable.Repeat("ArrowDown", Rows - 1).Concat(Enumerable.Repeat("ArrowUp", Rows - 1)))
        {
            await page.Keyboard.PressAsync(key);
            steps.Add(await FocusedAsync(page));
        }

        return steps;
    }

    /// <summary>That the menu's box is inside the viewport, which is <paramref name="width" /> by <paramref name="height" />.</summary>
    public static void AssertFits(Opened opened, int width, int height)
    {
        Assert.Equal(width, opened.ViewportWidth);
        Assert.Equal(height, opened.ViewportHeight);
        Assert.True(opened.X >= -0.5 && opened.Y >= -0.5, $"the menu starts outside the viewport: {opened}");
        Assert.True(opened.X + opened.Width <= width + 0.5, $"the menu runs past the viewport's right edge: {opened}");
        Assert.True(opened.Y + opened.Height <= height + 0.5, $"the menu runs past the viewport's bottom edge: {opened}");
    }

    private static async Task<Step> FocusedAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>("() => {" + Reader + """
                const m = menu(); const row = document.activeElement;
                return { key: row.getAttribute('data-rask-key') ?? row.textContent.trim(), inside: rows(m).includes(row) && inside(m, row) };
            }
            """);
        return new Step(json.GetProperty("key").GetString()!, json.GetProperty("inside").GetBoolean());
    }

    /// <summary>The open menu: its box, the viewport's size, how far it is scrolled, and its first row.</summary>
    public sealed record Opened(
        double X, double Y, double Width, double Height, double ViewportWidth, double ViewportHeight,
        double ScrollTop, bool Capped, bool FirstInside, bool FirstFocused);

    /// <summary>The row that has focus, by its key, and whether it is in the menu's box.</summary>
    public sealed record Step(string Key, bool Inside);
}
