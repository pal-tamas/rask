using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>Chromium, and the one way an example is measured: shared by measure, parity, sync and every check.</summary>
public static class Measure
{
    /// <summary>A preparation of a loaded page before it is measured: (page, scheme).</summary>
    public delegate Task Prepare(IPage page, string scheme);

    /// <summary>A preparation of a layout demo before it is measured: (page, scheme, section).</summary>
    public delegate Task PrepareLayout(IPage page, string scheme, string section);

    /// <summary>
    ///     Every <c>[data-preview-wrapper]</c> on <paramref name="url" />, measured in light and in dark.
    ///     <paramref name="prepare" /> runs on the loaded page before anything is measured. <paramref name="view" /> is the
    ///     viewport, for a page that is another thing on a phone (<c>[390, 844]</c>); a desktop's unless given.
    /// </summary>
    public static async Task<JsonObject> MeasurePage(IBrowser browser, string url, string shots, Prepare? prepare = null, int[]? view = null)
    {
        view ??= [1280, 900];
        var schemes = new JsonObject();
        foreach (var scheme in new[] { "light", "dark" })
        {
            // A reader who asks for less motion: the page is measured as it loads, and what its script moves on a timer of
            // its own is held there — Flux's autoplaying carousel (its docs: "disabled when reduced motion is preferred";
            // measured, it stays on its first slide), which moved on a loaded runner before its example was reached and
            // never on the static Rask page. Neither stylesheet changes for it: Flux stops no animation under reduced
            // motion, and the kit has no `motion-reduce` rule.
            var options = Screen.Of(view[0], view[1]);
            options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
            options.ReducedMotion = ReducedMotion.Reduce;
            var context = await browser.NewContextAsync(options);
            // A fixed clock: a calendar that opens on today would measure differently every morning.
            await context.Clock.SetFixedTimeAsync("2026-01-15T12:00:00Z");
            // Motion at rest (PageScripts.Rest), for Flux's page and Rask's alike: a look read mid-flight measures
            // differently every run.
            await context.AddInitScriptAsync($"window.__fluxRest = {PageScripts.Rest}");
            var page = await context.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            await page.Run("() => document.fonts.ready.then(() => undefined)");
            await page.Run("() => window.__fluxRest()");
            if (prepare is not null)
            {
                await prepare(page, scheme);
            }

            schemes[scheme] = await Examples(page, Path.Combine(shots, scheme));
            await context.CloseAsync();
        }

        return schemes;
    }

    /// <summary>
    ///     The examples of a loaded page. <paramref name="layout" />: a whole document rather than one example (see
    ///     <see cref="MeasureLayouts" />) — more controls have their states measured, and only those that are rendered.
    /// </summary>
    public static async Task<JsonArray> Examples(IPage page, string shots, bool layout = false)
    {
        Directory.CreateDirectory(shots);
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("DOM.enable");
        await cdp.SendAsync("CSS.enable");

        var wrappers = await page.QuerySelectorAllAsync("[data-preview-wrapper]");
        var examples = new JsonArray();
        for (var index = 0; index < wrappers.Count; index++)
        {
            var wrapper = wrappers[index];
            // A popup is closed as loaded, and the open popup is the component. An example says what opens it with
            // `data-parity-open` on that element — a press, or "hover" — and it is open while this example is measured.
            var opener = await wrapper.QuerySelectorAsync("[data-parity-open]");
            if (opener is not null)
            {
                await Open(page, wrapper, opener);
            }

            var example = (JsonObject)(await wrapper.Run(PageScripts.Collect,
                new { STYLES = Pressables.Styles, PRESSABLE = Pressables.Pressable, HELD = Pressables.Held, index, layout }))!;
            var targets = StateTargets(new Example(example).Nodes, layout);
            await page.Run(PageScripts.Baseline, new { STYLES = Pressables.Styles, targets });
            await wrapper.ScrollIntoViewIfNeededAsync();
            var name = $"{index.ToString("00", CultureInfo.InvariantCulture)}-{(Js.Str(example["section"]) is { Length: > 0 } s ? s : "intro")}";
            await Shoot(page, wrapper, Path.Combine(shots, $"{name}.png"));

            // Forced pseudo-states rather than real pointer moves: a hover that opens a tooltip or a menu would
            // move the page under the next measurement, and :focus-visible cannot be reached by el.focus().
            var states = new JsonArray();
            foreach (var target in targets)
            {
                var root = await cdp.SendAsync("DOM.getDocument", new Dictionary<string, object> { ["depth"] = 0 });
                var rootId = root!.Value.GetProperty("root").GetProperty("nodeId").GetInt32();
                var found = await cdp.SendAsync("DOM.querySelector", new Dictionary<string, object> { ["nodeId"] = rootId, ["selector"] = $"[data-m=\"{target}\"]" });
                var nodeId = found!.Value.GetProperty("nodeId").GetInt32();
                if (nodeId == 0)
                {
                    continue;
                }

                foreach (var state in new[] { "hover", "active", "focus-visible" })
                {
                    await cdp.SendAsync("CSS.forcePseudoState", new Dictionary<string, object>
                    {
                        ["nodeId"] = nodeId,
                        ["forcedPseudoClasses"] = state == "focus-visible" ? new[] { "focus", "focus-visible" } : new[] { state },
                    });
                    var changed = (JsonObject)(await page.Run(PageScripts.Diff, new { STYLES = Pressables.Styles, target }))!;
                    if (changed.Count > 0)
                    {
                        states.Add(new JsonObject { ["node"] = target, ["state"] = state, ["changed"] = changed });
                    }
                }

                await cdp.SendAsync("CSS.forcePseudoState", new Dictionary<string, object> { ["nodeId"] = nodeId, ["forcedPseudoClasses"] = Array.Empty<string>() });
            }

            example["states"] = states;
            if (opener is not null)
            {
                await Close(page);
            }

            examples.Add(example);
        }

        return examples;
    }

    /// <summary>
    ///     The nodes of one example whose hover, active and focus-visible states are measured: every component root and
    ///     part (<c>data-flux-*</c> on Flux's page, <c>data-ui-*</c> on a Rask one) and every native control, in document
    ///     order, up to a limit that keeps a long example affordable. From the recorded nodes rather than the live page, so
    ///     parity can ask the same question of a measurement taken earlier. A layout is a whole document: 400 controls
    ///     rather than 60, and none that is not rendered.
    /// </summary>
    public static List<string> StateTargets(IEnumerable<MeasuredNode> nodes, bool layout = false)
    {
        var controls = new HashSet<string>(StringComparer.Ordinal) { "button", "a", "input", "select", "textarea", "summary", "label" };
        static bool Marked(MeasuredNode node) => node.Attrs.Any(a => a.Key.StartsWith("data-flux", StringComparison.Ordinal) || a.Key.StartsWith("data-ui-", StringComparison.Ordinal));
        return nodes
            .Where(node => Marked(node) || controls.Contains(node.Tag) || node.Has("role") || node.Has("tabindex"))
            .Where(node => !(layout && node.Hidden))
            .Take(layout ? 400 : 60)
            .Select(node => node.Id)
            .ToList();
    }

    // Opened the way a reader opens it — a real pointer, at the element's centre — with the example mid-screen, so
    // the popup has the same room around it on both pages. On a phone a finger: a tap, for a press and a hover alike.
    private static async Task Open(IPage page, IElementHandle wrapper, IElementHandle opener)
    {
        await wrapper.Run("el => el.scrollIntoView({ block: 'center' })");
        var box = (await opener.BoundingBoxAsync())!;
        double[] at = [box.X + box.Width / 2, box.Y + box.Height / 2];
        var phone = page.ViewportSize!.Width < 640;
        var hover = await opener.GetAttributeAsync("data-parity-open") == "hover";
        // A hover popover is tapped by a finger that has been on the page before (FIRST_TOUCH in parity-popover).
        if (phone)
        {
            if (hover)
            {
                await page.Touchscreen.TapAsync(4, (float)at[1]);
            }

            // The hover a finger leaves on what it tapped (Screen.Tapped).
            await Screen.Tapped(page, opener, at);
            if (!hover)
            {
                await page.Run(PageScripts.Looked, at);
            }
        }
        else if (hover)
        {
            // A pointer comes from somewhere: Flux's page now and then does not open under one whose first move ever
            // lands on the trigger (its hover example measured closed, in one scheme of two). Parked first, then
            // brought over, and waited on until the popup shows — or a second of frames has gone by.
            await page.Mouse.MoveAsync(4, 4);
            await Screen.Frames(page);
            await page.Mouse.MoveAsync((float)(at[0] - 3), (float)(at[1] - 3));
            await page.Mouse.MoveAsync((float)at[0], (float)at[1]);
            await wrapper.Run(PageScripts.PopoverShown);
        }
        else
        {
            await page.Mouse.ClickAsync((float)at[0], (float)at[1]);
            // A popup that takes the pointer from the page (pointer-events: none on <html>, Flux's and rask-lock.ts's)
            // leaves the trigger under a pointer that no longer hovers it — once the browser looks again, which it does
            // on a timer of its own after the page changed: at once on a Mac, a while later on a loaded Linux runner,
            // where the two pages were measured on either side of it. The pointer is moved by a pixel (a move to where it
            // already is may be no move at all), so both look now — once the lock is ON: it lands when the page's script
            // gets to it (a toggle event, a hook), and a move made before that left the kit's chevron hovered on the runner
            // (2026-11-01, #1260) where a Mac had locked in time. So the move waits for the page to stop changing.
            await page.Run(PageScripts.Locked);
            await page.Mouse.MoveAsync((float)(at[0] + 1), (float)at[1]);
        }

        await Screen.Frames(page);
    }

    /// <summary>
    ///     What a reader sees of one example, shot beside its measurement. A wrapper with no box of its own is shot as the
    ///     viewport it shows in: on a phone toast-shown's frame is given its Flux twin's padding, none, around a toast fixed
    ///     to the viewport, and an element screenshot waits its whole 30 s for that box to show — for every example, twice:
    ///     the page took 20 minutes at 0% CPU, and read as hung. A shot is a picture for a person, so one that fails says
    ///     so and the measurement goes on.
    /// </summary>
    public static async Task Shoot(IPage page, IElementHandle wrapper, string path)
    {
        var box = await wrapper.BoundingBoxAsync();
        try
        {
            if (box is { Width: > 0, Height: > 0 })
            {
                await wrapper.ScreenshotAsync(new ElementHandleScreenshotOptions { Path = path, Timeout = 5000 });
            }
            else
            {
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Timeout = 5000 });
            }
        }
        catch (PlaywrightException error)
        {
            Console.Error.WriteLine($"flux: no screenshot {path}: {error.Message.Split('\n')[0]}");
        }
        catch (TimeoutException error)
        {
            Console.Error.WriteLine($"flux: no screenshot {path}: {error.Message.Split('\n')[0]}");
        }
    }

    private static async Task Close(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        if (page.ViewportSize!.Width >= 640)
        {
            await page.Mouse.MoveAsync(0, 0);
        }

        await Screen.Frames(page);
    }

    // ----- Layouts ---------------------------------------------------------------------------------------
    // A layout is a whole document, so Flux's docs do not render it in a `[data-preview-wrapper]`: under each
    // heading the page links to a full-screen demo (fluxui.dev/demo/<name>). One "example" of a layout page
    // is therefore one demo, at one width, in one state — its <body> measured exactly as a wrapper is.

    /// <summary>Desktop and phone, and what a reader can do to the sidebar at each: narrow it to its rail, slide it in.</summary>
    public static readonly (int Width, string[] States)[] Views = [(1280, ["", "rail"]), (390, ["", "open"])];

    // What puts a layout into a state, on either side: Flux's control or Rask's.
    private static readonly Dictionary<string, string> Controls = new(StringComparer.Ordinal)
    {
        ["rail"] = "[data-flux-sidebar-collapse] button, [data-ui-sidebar-collapse] label",
        ["open"] = "[data-flux-sidebar-toggle], [data-ui-sidebar-toggle]",
    };

    /// <summary>A layout's demo: its name, and the id of the &lt;h2&gt; above its link ('' for the first).</summary>
    public sealed record Demo(string Name, string Section);

    /// <summary>The demos a layout page links to, each under the id of the &lt;h2&gt; above it.</summary>
    public static async Task<List<Demo>> LayoutDemos(IBrowser browser, string url)
    {
        var page = await browser.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        var demos = await page.Run("""
            () => {
              const headings = [...document.querySelectorAll('h2[id]')];
              const seen = new Set();
              return [...document.querySelectorAll('a[href*="/demo/"]')].filter(a => !seen.has(a.href) && seen.add(a.href)).map(a => ({
                name: new URL(a.href).pathname.split('/').pop(),
                section: headings.filter(h => h.compareDocumentPosition(a) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '',
              }));
            }
            """);
        await page.CloseAsync();
        return (demos as JsonArray ?? []).Select(d => new Demo(Js.Str(d!["name"])!, Js.Str(d["section"]) ?? "")).ToList();
    }

    /// <summary>
    ///     Every demo at every width and state, in light and in dark. <paramref name="locate" /> turns a demo into its URL;
    ///     <paramref name="only" /> names the examples to take (a side is only asked for the states the other side has).
    /// </summary>
    public static async Task<JsonObject> MeasureLayouts(IBrowser browser, IReadOnlyList<Demo> demos, Func<Demo, string> locate, string shots,
        PrepareLayout? prepare = null, ISet<string>? only = null)
    {
        var schemes = new JsonObject();
        foreach (var scheme in new[] { "light", "dark" })
        {
            var examples = new JsonArray();
            schemes[scheme] = examples;
            foreach (var demo in demos)
            {
                foreach (var (width, states) in Views)
                {
                    foreach (var state in states)
                    {
                        var section = $"{(demo.Section.Length > 0 ? demo.Section : demo.Name)}@{width}{(state.Length > 0 ? $"-{state}" : "")}";
                        if (only is not null && !only.Contains(section))
                        {
                            continue;
                        }

                        var options = Screen.Of(width, 900);
                        options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
                        var context = await browser.NewContextAsync(options);
                        // Motion at rest, as MeasurePage has it: the sidebar's slide is measured where it ends.
                        await context.AddInitScriptAsync($"window.__fluxRest = {PageScripts.Rest}");
                        var page = await context.NewPageAsync();
                        await page.GotoAsync(locate(demo), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
                        await page.Run("() => document.fonts.ready.then(() => undefined)");
                        if (state.Length > 0)
                        {
                            var control = page.Locator(Controls[state]).Locator("visible=true").First;
                            if (await control.CountAsync() == 0)
                            {
                                await context.CloseAsync();
                                continue;
                            }

                            await control.ClickAsync();
                            // Away from the sidebar, and off the control: its hover is a state of its own, and the tooltip a
                            // focused control shows is not the layout.
                            await page.Mouse.MoveAsync(width - 5, 895);
                            await page.Run("() => document.activeElement?.blur()");
                            await page.WaitForTimeoutAsync(400);
                        }

                        await page.Run("""
                            name => {
                              document.body.setAttribute('data-preview-wrapper', '');
                              document.body.setAttribute('data-section', name);
                            }
                            """, section);
                        if (prepare is not null)
                        {
                            await prepare(page, scheme, section);
                        }

                        await page.Run("() => window.__fluxRest()");
                        foreach (var example in await Examples(page, Path.Combine(shots, scheme), true))
                        {
                            examples.Add(example!.DeepClone());
                        }

                        await context.CloseAsync();
                    }
                }
            }
        }

        return schemes;
    }
}
