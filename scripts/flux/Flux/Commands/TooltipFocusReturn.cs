using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Whether Flux's tooltip shows when focus comes BACK to its trigger: a menu button wearing a tooltip, its menu opened by
///     the mouse and closed by a click on an item, a click outside, or Escape — and, for contrast, a Tab onto it.
/// </summary>
/// <remarks>
///     Looks on Flux's live pages for a dropdown whose button is a tooltip's trigger. Where none is drawn, one is built inside
///     Flux's dropdown page from its own first dropdown: its button is wrapped in a &lt;ui-tooltip&gt; whose content takes the
///     classes of a live tooltip's, so Flux's own script runs both. Prints, per close and per viewport, whether the tooltip
///     shows after a settle, and whether the trigger matches :focus-visible.
///     <code>flux tooltip-focus-return [--view 390x844]</code>
/// </remarks>
[FluxCommand("tooltip-focus-return", "whether Flux's tooltip shows when focus returns to a menu button")]
public sealed class TooltipFocusReturn : IFluxCommand
{
    private static readonly string[] Slugs = ["dropdown", "table", "editor", "navbar", "button", "tooltip"];

    private const string Find = """
        () => {
          const hit = [...document.querySelectorAll('ui-dropdown')].find(d => d.querySelector(':scope > ui-tooltip, :scope > * > ui-tooltip'));
          if (hit) { hit.setAttribute('data-probe', ''); return 'drawn'; }
          return null;
        }
        """;

    // Wraps the first preview dropdown's button in a tooltip of Flux's own look.
    private const string Build = """
        (look) => {
          const dropdown = document.querySelector('[data-preview-wrapper] ui-dropdown');
          if (!dropdown) return null;
          const button = dropdown.querySelector(':scope > button');
          if (!button) return null;
          const tooltip = document.createElement('ui-tooltip');
          tooltip.setAttribute('data-flux-tooltip', '');
          tooltip.setAttribute('position', 'top center');
          const content = document.createElement('div');
          content.setAttribute('popover', 'manual');
          content.setAttribute('data-flux-tooltip-content', '');
          content.setAttribute('role', 'tooltip');
          content.className = look;
          content.textContent = 'Actions';
          button.replaceWith(tooltip);
          tooltip.append(button, content);
          dropdown.setAttribute('data-probe', '');
          return 'built';
        }
        """;

    private const string State = """
        () => {
          const probe = document.querySelector('[data-probe]');
          const content = probe.querySelector('[data-flux-tooltip-content]');
          const menu = probe.querySelector('[popover]:not([data-flux-tooltip-content])');
          const active = document.activeElement;
          const style = content ? getComputedStyle(content) : null;
          const box = content ? content.getBoundingClientRect() : null;
          return {
            tooltip: !!style && style.display !== 'none' && style.visibility !== 'hidden' && box.width > 0,
            menuOpen: !!menu && menu.matches(':popover-open'),
            active: active ? active.tagName.toLowerCase() + (active.closest('[data-probe]') ? ' (in probe)' : '') : null,
            focusVisible: !!active && active.matches(':focus-visible'),
            box: box ? [Math.round(box.x), Math.round(box.y), Math.round(box.width), Math.round(box.height)] : null,
            viewport: [innerWidth, innerHeight],
          };
        }
        """;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var view = View.Of(new Args(args));
        var browser = await Browsers.Chromium();
        var context = await browser.NewContextAsync(view.Context());
        var page = await context.NewPageAsync();
        string? how = null;
        foreach (var slug in Slugs)
        {
            await page.GotoAsync("https://fluxui.dev/components/" + slug, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            if ((await page.Run(Find))?.GetValue<string>() is { } drawn)
            {
                how = drawn + " on " + slug;
                break;
            }
        }

        if (how is null)
        {
            await page.GotoAsync("https://fluxui.dev/components/tooltip", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            var look = (await page.Run("() => document.querySelector('[data-flux-tooltip-content]').className"))!.GetValue<string>();
            await page.GotoAsync("https://fluxui.dev/components/dropdown", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            how = (await page.Run(Build, look))?.GetValue<string>() + " on dropdown";
        }

        Console.WriteLine($"flux tooltip-focus-return: {how}, view {view}");
        var trigger = page.Locator("[data-probe] ui-tooltip > button, [data-probe] > button").First;
        await trigger.ScrollIntoViewIfNeededAsync();
        var item = page.Locator("[data-probe] [popover]:not([data-flux-tooltip-content]) [role=menuitem], [data-probe] [popover]:not([data-flux-tooltip-content]) ui-menu-item, [data-probe] [popover]:not([data-flux-tooltip-content]) button").First;

        foreach (var close in new[] { "item", "outside", "escape" })
        {
            await page.Mouse.MoveAsync(2, 2);
            await page.Mouse.ClickAsync(2, 2);
            await page.WaitForTimeoutAsync(300);
            await trigger.ClickAsync();
            await page.WaitForTimeoutAsync(300);
            var opened = await page.Run(State);
            // Off the trigger and its tooltip, so only focus can be showing it.
            var menuBox = await item.BoundingBoxAsync();
            if (menuBox is not null)
            {
                await page.Mouse.MoveAsync(menuBox.X + menuBox.Width / 2, menuBox.Y + menuBox.Height / 2, new MouseMoveOptions { Steps = 4 });
            }

            await page.WaitForTimeoutAsync(200);
            switch (close)
            {
                case "item":
                    await item.ClickAsync();
                    break;
                case "outside":
                    await page.Mouse.ClickAsync(2, 300);
                    break;
                default:
                    await page.Keyboard.PressAsync("Escape");
                    break;
            }

            await page.Mouse.MoveAsync(2, 300, new MouseMoveOptions { Steps = 2 });
            await page.WaitForTimeoutAsync(600);
            Console.WriteLine($"  close by {close}: opened {opened?.ToJsonString()} → after {(await page.Run(State))?.ToJsonString()}");
        }

        await page.Mouse.ClickAsync(2, 2);
        await trigger.FocusAsync();
        await page.Run("() => document.activeElement.blur()");
        // Shift+Tab then Tab lands on the trigger from its neighbour, by the keyboard.
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);
        Console.WriteLine($"  Tab onto it: {(await page.Run(State))?.ToJsonString()}");

        // Opened by the keyboard and closed by Escape: what a keyboard reader comes back to.
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(600);
        Console.WriteLine($"  Enter then Escape: {(await page.Run(State))?.ToJsonString()}");

        await browser.CloseAsync();
        return 0;
    }
}
