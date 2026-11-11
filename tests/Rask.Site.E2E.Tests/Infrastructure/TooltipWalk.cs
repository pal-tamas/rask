using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     A menu button wearing a kit tooltip — <c>Ui.RowActions</c>' ⋯ — walked the way a reader walks it: reached by Tab
///     once (so the browser counts the focus a script later hands back as <c>:focus-visible</c>, as it does for anyone who
///     has used the keyboard on the page), then its menu opened with the mouse and closed by a pick, a click outside or
///     Escape. Shared by the site's suite (WASM) and the Server suite, so both hosts are held to the same walk.
/// </summary>
/// <remarks>
///     Measured on Flux UI's editor (2026-11-10, <c>flux tooltip-focus-return</c>): focus comes back to the button after
///     each of the three closes, and the tooltip does not show; a Tab onto the button shows it.
/// </remarks>
public static class TooltipWalk
{
    /// <summary>The three ways a reader closes a menu the mouse opened, at a desktop's width and a phone's.</summary>
    public static readonly TheoryData<int, string> Closes = new()
    {
        { 1280, "pick" }, { 1280, "outside" }, { 1280, "escape" },
        { 390, "pick" }, { 390, "outside" }, { 390, "escape" },
    };

    // Whether the tooltip around the trigger is showing: open in the top layer and drawn.
    private const string Shown = """
        trigger => {
          const content = trigger.closest('[data-ui-tooltip]').querySelector('[data-ui-tooltip-content]');
          return content.matches(':popover-open') && content.getBoundingClientRect().width > 0;
        }
        """;

    // Two frames and a moment: long enough for anything a focus or a toggle schedules to have run.
    private const string Settle = "() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(done, 250))))";

    /// <summary>
    ///     Tabs onto the trigger, opens its menu with the mouse, rests the pointer on <paramref name="item" />, closes the
    ///     menu the way <paramref name="close" /> says (<c>pick</c>, <c>outside</c>, <c>escape</c>), and asserts that focus
    ///     came back to the trigger with no tooltip showing, then or after a settle; and that one the pointer then shows
    ///     by passing over the trigger leaves with it. <paramref name="outside" /> is something inert to click.
    /// </summary>
    public static async Task CloseByMouseShowsNoTooltipAsync(IPage page, ILocator trigger, ILocator menu, ILocator item, ILocator outside, string close)
    {
        var away = (await outside.BoundingBoxAsync())!;
        await TabOntoAsync(page, trigger);
        var tabbed = await trigger.EvaluateAsync<bool>(Shown);
        await trigger.ClickAsync();
        await Expect(menu).ToBeVisibleAsync();
        await item.HoverAsync();
        switch (close)
        {
            case "pick":
                await item.ClickAsync();
                break;
            case "outside":
                // The mouse itself: while a menu is open the page is locked (data-rask-locked), and Playwright's click
                // would wait for the element to take the pointer, which a press outside never needs.
                await page.Mouse.ClickAsync(away.X + (away.Width / 2), away.Y + (away.Height / 2));
                break;
            default:
                await page.Keyboard.PressAsync("Escape");
                break;
        }

        await Expect(menu).ToBeHiddenAsync();
        await trigger.EvaluateAsync("t => new Promise((done, fail) => { const at = performance.now(); (function look() { "
            + "if (document.activeElement === t) done(); else if (performance.now() - at > 10000) fail(new Error('focus went to ' + document.activeElement.outerHTML.slice(0, 120))); "
            + "else requestAnimationFrame(look); })(); })");
        var atOnce = await trigger.EvaluateAsync<bool>(Shown);
        await page.EvaluateAsync(Settle);
        var settled = await trigger.EvaluateAsync<bool>(Shown);
        await trigger.HoverAsync();
        var hovered = await trigger.EvaluateAsync<bool>(Shown);
        // Off and at rest, then one move more: the runtime looks at a move at most once in 100 ms, as Flux does.
        await page.Mouse.MoveAsync(away.X + 2, away.Y + 2, new() { Steps = 3 });
        await page.EvaluateAsync(Settle);
        await page.Mouse.MoveAsync(away.X + 6, away.Y + 2);
        await page.EvaluateAsync(Settle);
        var left = await trigger.EvaluateAsync<bool>(Shown);

        Assert.True(tabbed, "a Tab onto the trigger did not show its tooltip");
        Assert.False(atOnce, $"the tooltip showed when focus came back to its button after the {close} close");
        Assert.False(settled, $"the tooltip appeared after a settle once focus came back after the {close} close");
        Assert.True(hovered, "the pointer over the trigger did not show its tooltip");
        Assert.False(left, $"the tooltip stayed after the pointer left, on a trigger whose focus the {close} close handed back");
    }

    /// <summary>
    ///     Reaches the trigger by Tab and asserts that its tooltip shows, inside the viewport, and on top at its own
    ///     centre — not clipped by the scroller or the sticky cell around its trigger.
    /// </summary>
    public static async Task TabShowsTooltipInViewAsync(IPage page, ILocator trigger)
    {
        await TabOntoAsync(page, trigger);
        await page.EvaluateAsync(Settle);

        var focused = await trigger.EvaluateAsync<bool>("t => document.activeElement === t && t.matches(':focus-visible')");
        var shown = await trigger.EvaluateAsync<bool>(Shown);
        var box = await trigger.EvaluateAsync<double[]>("""
            t => {
              const content = t.closest('[data-ui-tooltip]').querySelector('[data-ui-tooltip-content]');
              const r = content.getBoundingClientRect();
              const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
              return [r.left, r.top, r.right, r.bottom, innerWidth, innerHeight, hit && content.contains(hit) ? 1 : 0];
            }
            """);

        Assert.True(focused, "Tab did not land on the trigger");
        Assert.True(shown, "a Tab onto the trigger did not show its tooltip");
        Assert.True(box[0] >= 0 && box[1] >= 0 && box[2] <= box[4] && box[3] <= box[5],
            $"the tooltip runs off the {box[4]}x{box[5]} viewport: [{box[0]}, {box[1]}, {box[2]}, {box[3]}]");
        Assert.True(box[6] == 1, "the tooltip is not what draws at its own centre: something clips or covers it");
    }

    // A button held at the page's corner, out of the layout and outside the tooltip's wrapper, focused and left by Tab,
    // which lands on the trigger.
    private static async Task TabOntoAsync(IPage page, ILocator trigger)
    {
        await trigger.EvaluateAsync("t => { const b = document.createElement('button'); b.id = '__before'; "
            + "b.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px'; t.closest('[data-ui-tooltip]').before(b); b.focus(); }");
        await page.Keyboard.PressAsync("Tab");
        await page.EvaluateAsync("() => document.getElementById('__before').remove()");
    }
}
