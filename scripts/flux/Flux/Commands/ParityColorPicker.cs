using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds the OPEN color picker to Flux UI's, example by example, and then walks both.
/// </summary>
/// <remarks>
///     Parity measures a page as it loads, and a picker loads closed: its popover is in the tree and not displayed. The
///     popover is most of the component, so this measures Flux's page again with each picker open — <see cref="Measure" />
///     opens whatever carries <c>data-parity-open</c>, by a real press at its centre, and closes it again before the next —
///     and files the result as the page <c>color-picker-open</c>, under the same section ids. ColorPickerOpenParity writes
///     the Rask page of that name with the same mark, and parity compares the two as it compares any page: the popover's
///     box, look and contents, and where it sits against its trigger.
///     <para>
///         <c>--live &lt;url&gt;</c> then walks a running site's color picker page (the URL of /ui/color-picker/) beside
///         Flux's page: the same keys and the same presses on both, and after each step what it left behind — whether the
///         popover is open, the value, what the inputs say, the preview, where the thumbs stand, which swatch is picked and
///         which holds the tab stop, what was announced and where focus is. <c>--phone</c> walks the sheet at 390 px.
///     </para>
///     <para>
///         <c>--sheet</c> opens the same examples on a phone (390 and 360 wide, light and dark), where the popover is Flux's
///         sheet, and holds every node's box inside the dialog and its layout styles to Flux's: parity measures at one
///         width, and the sheet is not at that one.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages        # writes the Rask page
///     flux parity-color-picker [--refresh] [--all]                     # --refresh re-measures Flux
///     flux parity-color-picker --section dropper
///     flux parity-color-picker --sheet
///     flux parity-color-picker --live &lt;url of /ui/color-picker/&gt; [--phone] [--wait 2200] [--walk &lt;name&gt;] [--all]
///     </code>
/// </remarks>
[FluxCommand("parity-color-picker", "the open color picker against Flux's, its phone sheet and its --live walks")]
public sealed class ParityColorPicker : IFluxCommand
{
    private const string Flux = "https://fluxui.dev/components/color-picker";

    // Runs in the page. Marks what a reader presses to open each example's first picker: the swatch button of an input
    // trigger, or the button trigger itself.
    private const string Mark = """
        function mark() {
          for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) {
            wrapper.querySelector('[data-flux-color-picker-swatch-button], [data-flux-color-picker-button]')?.setAttribute('data-parity-open', '');
          }
        }
        """;

    // Runs in the page, on a picker. Where the sheet is on the screen, then every node inside it, measured from the
    // sheet's own corner.
    private const string Sheet = """
        (root, p) => {
          const dialog = root.querySelector('dialog');
          const origin = dialog.getBoundingClientRect();
          const style = getComputedStyle(dialog);
          const half = n => Math.round(n * 2) / 2;
          const out = [`dialog at ${[origin.x, origin.y, origin.width, origin.height].map(Math.round).join(',')} ${style.position} max-height ${style.maxHeight} overflow ${style.overflowY} border ${style.borderWidth}`
            + ` shadow ${style.boxShadow.replace(/rgba\(0, 0, 0, 0\) 0px 0px 0px 0px, /g, '')} backdrop ${getComputedStyle(dialog, '::backdrop').backgroundColor.replace('oklab(0 0 0 / 0.1)', 'rgba(0, 0, 0, 0.1)')}`
            + ` page ${getComputedStyle(document.documentElement).overflowY}`];
          const walk = (el, depth) => {
            // Not Flux's nodes: the bound fields of the surfaces, and the box that tells the page it is the sheet.
            if ((el.tagName === 'INPUT' && el.type === 'hidden') || el.hasAttribute('data-ui-color-picker-sheet')) return;
            const box = el.getBoundingClientRect();
            const s = getComputedStyle(el);
            const marks = [...el.attributes].map(a => a.name).filter(n => n.startsWith(p) && n !== 'data-flux-allow-scroll').map(n => n.slice(p.length)).join(',');
            out.push(`${'  '.repeat(depth)}${el.tagName.toLowerCase().replace(/^ui-.*/, 'div')}[${marks}] ${[box.x - origin.x, box.y - origin.y, box.width, box.height].map(half).join(',')}`
              + ` ${s.display} padding ${s.padding} font ${s.fontSize} radius ${s.borderRadius} opacity ${s.opacity} border ${s.borderWidth} gap ${s.gap}`);
            if (el.tagName !== 'svg') for (const child of el.children) walk(child, depth + 1);
          };
          walk(dialog, 0);
          return out;
        }
        """;

    // Runs in the page, on a picker. What a step left behind, in words both pages can say. A thumb's place is read from
    // where it is DRAWN — Flux writes a percentage, the kit a custom property — and the value from what the inputs and
    // the preview show.
    private const string Observe = """
        (root, p) => {
          const one = name => root.querySelector(`[${p}${name}]`);
          const dialog = root.querySelector('dialog');
          const area = one('color-picker-area');
          const thumb = one('color-picker-area-thumb');
          const place = (el, box) => (el && box ? (() => {
            const a = el.getBoundingClientRect(); const b = box.getBoundingClientRect();
            return b.width ? `${Math.round((a.x + a.width / 2 - b.x) / b.width * 100)}/${Math.round((a.y + a.height / 2 - b.y) / b.height * 100)}` : '-';
          })() : '-');
          const swatches = [...root.querySelectorAll(`[${p}color-picker-swatch]`)];
          const active = document.activeElement;
          const focus = !active || active === document.body ? 'body'
            : swatches.includes(active) ? `swatch ${swatches.indexOf(active)}`
            : active.getAttribute('aria-label') || active.tagName.toLowerCase();
          const input = one('color-picker-input');
          const popoverInput = one('color-picker-popover-input');
          const shown = one('color-picker-value');
          return [
            dialog.open ? 'open' : 'shut',
            `input=${input ? input.value : shown ? shown.textContent.trim() : ''}`,
            `popover=${popoverInput ? popoverInput.value : ''}`,
            `preview=${getComputedStyle(one('color-picker-preview')).backgroundColor}`,
            `area=${dialog.open ? place(thumb, area) : '-'}`,
            `ranges=${[...root.querySelectorAll('input[type=range]')].map(r => r.value).join('/')}`,
            `picked=${swatches.findIndex(s => s.getAttribute('aria-selected') === 'true')}`,
            `stop=${swatches.findIndex(s => s.getAttribute('tabindex') === '0')}`,
            `said=${one('color-picker-announce').textContent.trim()}`,
            `expanded=${[...root.querySelectorAll('[aria-expanded]')].map(e => e.getAttribute('aria-expanded')).join(',')}`,
            `focus=${focus}`,
          ].join(' | ');
        }
        """;

    // A picker's parts, by name: each the selector it has under a kit's prefix.
    private static readonly Dictionary<string, Func<string, string>> Parts = new(StringComparer.Ordinal)
    {
        ["opener"] = p => $"[{p}color-picker-swatch-button], [{p}color-picker-button]",
        ["input"] = p => $"[{p}color-picker-input]",
        ["popover-input"] = p => $"[{p}color-picker-popover-input]",
        ["area"] = p => $"[{p}color-picker-area]",
        ["area-thumb"] = p => $"[{p}color-picker-area-thumb]",
        ["hue"] = p => $"[{p}color-picker-slider][data-channel=hue]",
        ["alpha"] = p => $"[{p}color-picker-slider][data-channel=alpha]",
        ["clear"] = _ => "[aria-label=\"Clear color\"]",
        ["copy"] = p => $"[{p}color-picker-copy]",
        // The box Flux hears its Select through, and the one around its Cancel: the kit's are the same two, in that order.
        ["cancel"] = p => p == "data-flux-" ? "ui-close button" : "dialog [data-ui-button]:not([data-ui-group-target])",
        ["select"] = p => p == "data-flux-" ? "ui-color-picker-select" : "dialog [role=button]",
    };

    // `Flux` is the picker's place among Flux's; `Rask` the id of the showcase's example.
    private sealed record PickerWalk(string Name, int Flux, string Rask, IReadOnlyList<string> Steps, int Nth = 0);

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        if (arg.Option("live") is { } live)
        {
            var browser = await Browsers.Chromium();
            var differs = await WalkBoth(browser, arg, live, arg.Flag("phone"), double.Parse(arg.Option("wait") ?? "400", CultureInfo.InvariantCulture));
            await browser.CloseAsync();
            Console.WriteLine(differs > 0 ? $"\nflux parity: the color picker behaves differently in {differs} walk(s)." : "\nflux parity: the color picker walks as Flux does.");
            return differs > 0 ? 1 : 0;
        }

        if (arg.Flag("sheet"))
        {
            var browser = await Browsers.Chromium();
            var differs = await Sheets(browser);
            await browser.CloseAsync();
            Console.WriteLine(differs > 0 ? $"\nflux parity: the color picker's sheet differs in {differs} case(s)." : "\nflux parity: the color picker's sheet matches Flux.");
            return differs > 0 ? 1 : 0;
        }

        var output = Repo.Parity("flux", "color-picker-open");
        var file = Path.Combine(output, "measurements.json");
        if (!File.Exists(file) || arg.Flag("refresh"))
        {
            var browser = await Browsers.Chromium();
            var open = await Measure.MeasurePage(browser, Flux, output, (page, _) => page.Run(Mark));
            await browser.CloseAsync();
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(file, Js.Stringify(open));
            Console.WriteLine($"flux: {(open["light"] as JsonArray)?.Count} color-picker example(s) measured open");
        }

        return await Subcommand.Run<Parity>(["color-picker-open", .. arg.All.Where(a => a != "--refresh")]);
    }

    // ----- The sheet ---------------------------------------------------------------------------------------

    // Each example's place among Flux's pickers and on the parity page, which writes the same eleven in order (the
    // "sizes" example holds three).
    private static async Task<int> Sheets(IBrowser browser)
    {
        (int Index, string Scheme, int Width)[] cases =
        [
            (0, "light", 390), (0, "dark", 390), (0, "light", 360), (1, "light", 390), (1, "dark", 390), (5, "light", 390), (6, "light", 390),
            (7, "light", 390), (8, "light", 390), (9, "light", 390), (10, "light", 390), (11, "dark", 390),
        ];
        var differs = 0;
        foreach (var (index, scheme, width) in cases)
        {
            var flux = await SheetOf(browser, true, index, scheme, width);
            var rask = await SheetOf(browser, false, index, scheme, width);
            var bad = new List<string>();
            for (var i = 0; i < Math.Max(flux.Count, rask.Count); i++)
            {
                var (a, b) = (i < flux.Count ? flux[i] : "undefined", i < rask.Count ? rask[i] : "undefined");
                if (a != b)
                {
                    bad.Add($"       flux {a}\n       rask {b}");
                }
            }

            differs += bad.Count > 0 ? 1 : 0;
            Console.WriteLine($"{(bad.Count > 0 ? "FAIL" : "ok  ")} sheet #{index} {scheme} {width} ({flux.Count} nodes)");
            foreach (var line in bad.Take(8))
            {
                Console.WriteLine(line);
            }
        }

        return differs;
    }

    private static async Task<List<string>> SheetOf(IBrowser browser, bool flux, int index, string scheme, int width)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 844 },
            HasTouch = true,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(flux ? Flux : Repo.FileUrl(Repo.Parity("rask", "color-picker-open.html")), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        if (scheme == "dark")
        {
            await page.Run("() => document.documentElement.classList.add('dark')");
        }

        var prefix = flux ? "data-flux-" : "data-ui-";
        var picker = page.Locator($"[{prefix}color-picker]").Nth(index);
        await picker.ScrollIntoViewIfNeededAsync();
        await picker.Locator($"[{prefix}color-picker-swatch-button], [{prefix}color-picker-button]").First.TapAsync();
        await picker.Locator("dialog[open]").WaitForAsync();
        var rows = (await picker.Run(Sheet, prefix) as JsonArray ?? []).Select(row => Js.Str(row) ?? "").ToList();
        await context.CloseAsync();
        return rows;
    }

    // ----- The keyboard and the pointer, on a running app --------------------------------------------------

    private static PickerWalk[] Walks(bool phone) => phone
        ?
        [
            new("the sheet holds until Select", 0, "intro",
            [
                "press:opener", "at:area:0.2:0.2", "at:hue:0.5:0.5", "fill:popover-input:#00ff00", "key:Enter", "press:select", "press:opener", "at:area:0.6:0.6", "press:cancel",
                "press:opener", "at:area:0.8:0.3", "key:Escape", "press:opener", "at:area:0.3:0.8", "outside", "press:opener", "swatch:3", "press:opener", "focus:area-thumb",
                "key:ArrowRight", "key:Escape", "fill:input:#ff0000", "key:Enter",
            ]),
            new("the sheet, button trigger", 5, "button", ["press:opener", "at:hue:0.25:0.5", "press:select", "press:opener", "swatch:1"]),
        ]
        :
        [
            new("area keys", 0, "intro",
                ["press:opener", "key:ArrowRight", "key:ArrowDown", "key:Shift+ArrowLeft", "key:Shift+ArrowUp", "key:PageUp", "key:PageDown", "key:Home", "key:End", "key:Escape"]),
            new("hue keys", 0, "intro",
            [
                "press:opener", "key:Tab", "key:ArrowRight", "key:ArrowLeft", "key:Shift+ArrowRight", "key:Shift+ArrowLeft", "key:Home", "key:End", "key:Shift+ArrowRight",
                "key:PageDown", "key:PageUp", "key:Escape",
            ]),
            new("the trigger input", 0, "intro",
            [
                "focus:input", "key:ArrowDown", "key:Escape", "fill:input:#f00", "key:Enter", "fill:input:rgb(1 2 3)", "blur:input",
                "fill:input:hsl(120 50% 50% / 0.5)", "key:Enter", "fill:input:blue", "key:Enter", "fill:input:oklch(0.7 0.1 200)", "key:Enter", "fill:input:lab(50 40 30)",
                "key:Enter", "fill:input:", "key:Enter", "press:opener", "key:ArrowRight", "key:Escape",
            ]),
            new("the popover input", 0, "intro",
                ["press:opener", "fill:popover-input:#00f", "key:Enter", "fill:popover-input:lime", "blur:popover-input", "fill:popover-input:", "key:Enter", "key:Escape"]),
            new("swatches", 0, "intro",
            [
                "press:opener", "key:Tab", "key:Tab", "key:Tab", "key:ArrowDown", "key:ArrowDown", "key:ArrowRight", "key:ArrowUp", "key:ArrowLeft", "key:End", "key:ArrowRight",
                "key:Space", "key:Home", "key:Enter", "swatch:5", "key:Escape", "press:opener", "swatch:3", "outside",
            ]),
            new("pointer", 0, "intro",
            [
                "press:opener", "at:area:0.5:0.5", "at:area:0.2:0.9", "drag:area:0.2:0.2:0.9:0.9", "drag:area:0.5:0.5:1.5:-0.5", "at:hue:0.5:0.5", "at:hue:0.02:0.5",
                "drag:hue:0.2:0.5:0.8:0.5", "key:ArrowRight", "key:Escape",
            ]),
            new("alpha", 1, "format",
            [
                "press:opener", "key:Tab", "key:Tab", "key:ArrowRight", "key:Shift+ArrowLeft", "key:Home", "key:End", "at:alpha:0.5:0.5", "swatch:0",
                "fill:popover-input:#ff000080", "key:Enter", "key:Escape",
            ], Nth: 3),
            new("button trigger", 5, "button",
                ["press:opener", "key:ArrowRight", "key:Escape", "key:Enter", "key:Escape", "key:Space", "key:Escape", "key:ArrowDown", "swatch:1", "key:Escape"]),
            new("custom layout", 8, "layout", ["press:opener", "key:Tab", "key:ArrowLeft", "key:Tab", "key:ArrowRight", "key:Tab", "key:Escape"]),
            new("clearable", 10, "clearable",
                ["press:clear", "press:opener", "key:ArrowRight", "key:Tab", "key:ArrowRight", "swatch:2", "key:Escape", "press:clear", "fill:input:#123", "key:Enter"]),
            new("copyable", 11, "copyable", ["press:copy", "clipboard", "press:opener", "swatch:4", "key:Escape", "press:copy", "clipboard"]),
            new("disabled", 12, "disabled", ["force:opener", "force:input"]),
        ];

    private static async Task<int> WalkBoth(IBrowser browser, Args arg, string liveUrl, bool phone, double wait)
    {
        var differs = 0;
        foreach (var walk in Walks(phone).Where(w => arg.Option("walk") is not { } only || w.Name == only))
        {
            var sides = new List<List<string>>();
            foreach (var side in new[] { "flux", "rask" })
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = phone ? new ViewportSize { Width = 390, Height = 844 } : new ViewportSize { Width = 1280, Height = 900 },
                    HasTouch = phone ? true : null,
                    Permissions = ["clipboard-read", "clipboard-write"],
                });
                var page = await context.NewPageAsync();
                page.SetDefaultTimeout(15000);
                await page.GotoAsync(side == "flux" ? Flux : liveUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                var prefix = side == "flux" ? "data-flux-" : "data-ui-";
                var picker = side == "flux"
                    ? page.Locator("ui-color-picker").Nth(walk.Flux)
                    // Each example's result carries its testid: ui-color-picker, then ui-color-picker-<name>; the alpha walk
                    // takes the rgba picker, the fourth of the format example's six.
                    : page.Locator($"[data-testid=\"ui-color-picker{(walk.Rask == "intro" ? "" : "-" + walk.Rask)}\"] [data-ui-color-picker]").Nth(walk.Nth);
                // A WASM site boots after its document has loaded, and draws its page of demos a moment after that.
                await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 90000 });
                if (side == "rask")
                {
                    await page.WaitForTimeoutAsync((float)(wait * 2));
                }

                // Into view by the page's own scroll: a page whose other demos never come to rest is never "stable" for Playwright.
                await picker.Run("el => el.scrollIntoView({ block: 'center' })");
                var lines = new List<string>();
                foreach (var step in walk.Steps)
                {
                    try
                    {
                        await Act(page, picker, prefix, step, phone);
                    }
                    catch (Exception error) when (error is PlaywrightException or TimeoutException or InvalidOperationException)
                    {
                        lines.Add($"{step}: could not ({ParityComparer.Slice(error.Message.Split('\n')[0], 80)})");
                    }

                    // Flux's page settles a step a moment after it — its focus after a touch on a slider most of all.
                    await page.WaitForTimeoutAsync(side == "flux" ? 400 : (float)wait);
                    string seen;
                    if (step == "clipboard")
                    {
                        seen = Js.Str(await page.Run("() => navigator.clipboard.readText()")) ?? "";
                    }
                    else
                    {
                        try
                        {
                            seen = Js.Str(await picker.Run(Observe, prefix)) ?? "";
                        }
                        catch (Exception error) when (error is PlaywrightException or TimeoutException)
                        {
                            seen = $"nothing to observe ({ParityComparer.Slice(error.Message.Split('\n')[0], 60)})";
                        }
                    }

                    lines.Add($"{step} -> {seen}");
                }

                sides.Add(lines);
                await context.CloseAsync();
            }

            var (flux, rask) = (sides[0], sides[1]);
            string RaskAt(int i) => i < rask.Count ? rask[i] : "undefined";
            var bad = flux.Select((line, i) => line == RaskAt(i) && !arg.Flag("all") ? null : $"         flux {line}\n         rask {RaskAt(i)}").OfType<string>().ToList();
            var wrong = flux.Where((line, i) => line != RaskAt(i)).Count();
            differs += wrong > 0 ? 1 : 0;
            Console.WriteLine($"{(wrong > 0 ? "FAIL" : "ok  ")} {walk.Name} ({walk.Steps.Count} steps){(wrong > 0 ? $" — {wrong} differ" : "")}");
            foreach (var line in bad)
            {
                Console.WriteLine(line);
            }
        }

        return differs;
    }

    private static async Task Act(IPage page, ILocator picker, string prefix, string step, bool phone)
    {
        var parts = step.Split(':');
        var (verb, rest) = (parts[0], parts[1..]);
        ILocator Part(string name) => picker.Locator(Parts[name](prefix)).First;
        Task Press(ILocator locator) => phone ? locator.TapAsync() : locator.ClickAsync();
        async Task<(float X, float Y)> Point(string name, string x, string y)
        {
            var box = (await Part(name).BoundingBoxAsync())!;
            return ((float)(box.X + (box.Width * Js.Parse(x))), (float)(box.Y + (box.Height * Js.Parse(y))));
        }

        switch (verb)
        {
            case "press":
                await Press(Part(rest[0]));
                break;
            case "force":
                try
                {
                    await Part(rest[0]).ClickAsync(new LocatorClickOptions { Force = true, Timeout = 1000 });
                }
                catch (Exception error) when (error is PlaywrightException or TimeoutException)
                {
                    // a disabled control takes no press: that is what is held
                }

                break;
            case "focus":
                await Part(rest[0]).FocusAsync();
                break;
            // Left for nothing, which is the same nothing on both pages: Tab would land on whatever the page has next.
            case "blur":
                await Part(rest[0]).BlurAsync();
                break;
            case "fill":
                await Part(rest[0]).FillAsync(string.Join(':', rest[1..]));
                break;
            case "key":
                await page.Keyboard.PressAsync(rest[0]);
                break;
            case "swatch":
                await Press(picker.Locator($"[{prefix}color-picker-swatch]").Nth(int.Parse(rest[0], CultureInfo.InvariantCulture)));
                break;
            case "outside":
                await (phone ? page.Touchscreen.TapAsync(195, 10) : page.Mouse.ClickAsync(5, 5));
                break;
            case "clipboard":
                break;
            case "at":
                var (x, y) = await Point(rest[0], rest[1], rest[2]);
                await (phone ? page.Touchscreen.TapAsync(x, y) : page.Mouse.ClickAsync(x, y));
                break;
            case "drag":
                var from = await Point(rest[0], rest[1], rest[2]);
                var to = await Point(rest[0], rest[3], rest[4]);
                await page.Mouse.MoveAsync(from.X, from.Y);
                await page.Mouse.DownAsync();
                await page.Mouse.MoveAsync((from.X + to.X) / 2, (from.Y + to.Y) / 2, new MouseMoveOptions { Steps = 4 });
                await page.Mouse.MoveAsync(to.X, to.Y, new MouseMoveOptions { Steps = 4 });
                await page.Mouse.UpAsync();
                break;
            default:
                throw new InvalidOperationException($"unknown step {step}");
        }
    }
}
