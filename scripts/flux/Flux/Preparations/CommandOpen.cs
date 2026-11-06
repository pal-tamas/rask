using Microsoft.Playwright;

namespace Rask.Flux.Preparations;

/// <summary>
///     The command palette once it is used, for <c>flux parity command-open --prepare command-open</c> (parity-command
///     runs it). A palette as loaded shows no lit command, no filter, no clear button and no open modal, so both pages are
///     brought to those states — the same way, with the same keys and presses — before they are measured. Flux's page
///     holds ONE inline palette, so its first example is copied twice (its &lt;ui-select&gt; is a custom element, and a
///     copy is one in its own right); CommandOpenParity writes the Rask page with three.
/// </summary>
/// <remarks>
///     typed — "ch" typed, the box left: one command shown, the clear button there, nothing lit;
///     empty — "zz" typed: no command shown, "No results found";
///     as-a-modal — the modal opened from its trigger, the box pressed, ArrowDown: the second command lit, the close
///     button in the box's corner.
/// </remarks>
[ParityPreparation("command-open")]
public sealed class CommandOpen : IParityPreparation
{
    private static readonly string[] Sections = ["typed", "empty", "as-a-modal"];
    private const string Box = "[data-flux-command] input, [data-ui-command] input";
    private const string Trigger = "[data-flux-modal-trigger] button, [data-ui-modal-trigger] button";

    /// <inheritdoc />
    public string Flux(string slug) => "command";

    /// <summary>
    ///     A browser that SHOWS its scrollbars, as a reader's does (parity-modal): headless hides them, and there Flux's page
    ///     lock reserves a gutter nothing was using, so its modal sits 7.5px off centre and the kit's does not.
    /// </summary>
    public BrowserTypeLaunchOptions Launch => new() { IgnoreDefaultArgs = ["--hide-scrollbars"] };

    /// <summary>What the Rask page is given of the runtime, as an app has it: the hooks, which the palette and the modal ask for.</summary>
    public IReadOnlyList<string> Runtime => ["rask-hooks.ts"];

    /// <inheritdoc />
    public async Task Prepare(IPage page, string scheme, string side)
    {
        // On a phone (parity --view 390x844) a press is a tap.
        var phone = page.ViewportSize!.Width < 640;
        async Task Press(IElementHandle el)
        {
            if (phone)
            {
                await el.TapAsync();
            }
            else
            {
                await el.ClickAsync();
            }
        }

        if (side == "flux")
        {
            await page.Run("""
                sections => {
                  const [inline, modal] = document.querySelectorAll('[data-preview-wrapper]');
                  for (const section of sections.slice(0, 2)) {
                    const copy = inline.cloneNode(true);
                    copy.dataset.section = section;
                    modal.before(copy);
                  }
                  inline.removeAttribute('data-preview-wrapper');
                  modal.dataset.section = sections[2];
                }
                """, Sections);
            await Settle(page);
        }

        // More page than the viewport, as Flux's has: it scrolls, and an open modal is centred beside the scrollbar's gutter.
        if (side == "rask")
        {
            await page.Run("() => { document.body.style.paddingBottom = '100vh'; }");
        }

        var wrappers = await page.QuerySelectorAllAsync("[data-preview-wrapper]");
        foreach (var (nth, text) in new[] { (0, "ch"), (1, "zz") })
        {
            await Press((await wrappers[nth].QuerySelectorAsync(Box))!);
            await page.Keyboard.TypeAsync(text, new KeyboardTypeOptions { Delay = 40 });
            await page.Run("() => document.activeElement?.blur()");
            await Settle(page);
        }

        await Press((await wrappers[2].QuerySelectorAsync(Trigger))!);
        await Settle(page);
        await Press((await page.QuerySelectorAsync($"dialog[open] {string.Join(", dialog[open] ", Box.Split(", "))}"))!);
        await page.Keyboard.PressAsync("ArrowDown");
        if (!phone)
        {
            await page.Mouse.MoveAsync(1, 1);
        }

        await Settle(page);
    }

    // Settled: two frames, and what they started given the time to land.
    private static Task Settle(IPage page) =>
        page.Run("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(done, 250))))");
}
