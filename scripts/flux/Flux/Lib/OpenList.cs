using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>
///     Holds a control's OPEN list of options to Flux UI's live documentation. parity measures every example as loaded,
///     where a drawn list is not displayed; this opens each one — on fluxui.dev and on the page FluxParityPages wrote —
///     and compares what is then on screen: the control and the whole popup subtree (tag, box, computed styles,
///     pseudo-elements); where the popup sits against its trigger (every box is measured from the trigger's corner); a
///     row hovered, and a row pressed. <c>--live &lt;url&gt;</c> walks the keyboard and the pointer through Flux's examples
///     and through a running site, and compares what each step left behind.
/// </summary>
/// <remarks>
///     One module for every control that opens a list: parity-select, parity-autocomplete and parity-pillbox each hand
///     it an <see cref="OpenKind" /> — which page, which selectors, which walks — and call <see cref="Run" />.
/// </remarks>
public static class OpenList
{
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "width", "height" };

    /// <summary>
    ///     A static page has no runtime to open a list from an input: after the press, the popover is shown as the runtime
    ///     shows it. Only where the press landed: a control held out of view opens on neither page.
    /// </summary>
    public static async Task Shown(IPage page, double[] point, string how)
    {
        await page.Mouse.ClickAsync((float)point[0], (float)point[1]);
        await page.Run("""
            ([x, y]) => {
              const { panel, trigger } = window.__placed;
              if (!panel.matches(':popover-open') && trigger.contains(document.elementFromPoint(x, y))) panel.showPopover();
            }
            """, point);
    }

    /// <summary>The check, on the command line it was given: answers the exit code.</summary>
    public static async Task<int> Run(OpenKind kind, string[] args)
    {
        var arg = new Args(args);
        var output = Repo.Parity();
        // `--view 390x844`: every example opened on a phone (Screen.Of), its Flux side kept under its own name.
        var view = View.Of(arg);
        var fluxUrl = $"https://fluxui.dev/components/{kind.Slug}";
        var raskPage = Path.Combine(output, "rask", $"{kind.RaskPage ?? kind.Slug}.html");
        if (!File.Exists(raskPage) && !arg.Flag("record") && arg.Option("live") is null)
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        var browser = await Browsers.Chromium();
        if (arg.Option("live") is not null || arg.Flag("record"))
        {
            // `--walk <name>` runs one walk. `--wait <ms>` between steps: a Debug WASM site takes over a second to draw a page of demos again.
            var wait = int.Parse(arg.Option("wait") ?? "450", CultureInfo.InvariantCulture);
            var differs = await WalkBoth(browser, kind, fluxUrl, arg.Option("live"), arg.Flag("all") || arg.Flag("record"), wait, arg.Option("walk"), view);
            await browser.CloseAsync();
            Console.WriteLine(differs > 0 ? $"\nflux parity: the {kind.Name}'s keyboard differs in {differs} walk(s)." : $"\nflux parity: the {kind.Name} walks as Flux does.");
            return differs > 0 ? 1 : 0;
        }

        var folder = $"{kind.Slug}-open";
        var fluxFile = Path.Combine(output, "flux", folder, $"measurements{view.Sized}.json");
        JsonObject flux;
        if (File.Exists(fluxFile) && !arg.Flag("refresh"))
        {
            flux = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(fluxFile))!;
        }
        else
        {
            flux = await MeasureOpen(browser, kind, fluxUrl, Path.Combine(output, "flux", folder + view.Sized), view, null);
            Directory.CreateDirectory(Path.Combine(output, "flux", folder));
            await File.WriteAllTextAsync(fluxFile, Js.Stringify(flux));
        }

        var rask = await MeasureOpen(browser, kind, kind.RaskUrl ?? Repo.FileUrl(raskPage), Path.Combine(output, "rask", folder + view.Sized), view, flux);
        Directory.CreateDirectory(Path.Combine(output, "rask", folder));
        await File.WriteAllTextAsync(Path.Combine(output, "rask", folder, $"measurements{view.Sized}.json"), Js.Stringify(rask));
        // The same width on both pages: Flux's control fills its column, and a held one has no column.
        string Held(string marker) => string.Join(", ", kind.Root.Split(", ").Where(root => root.Contains(marker, StringComparison.Ordinal)).Select(root => $"[data-preview-wrapper] {root}:has({kind.Popup})"));
        // Where the list lands is held on a phone and a desktop alike by the desktop run (Placement.Views).
        var astray = kind.Edges is not null && view.Sized.Length == 0 ? await Placement.Landings(browser, new Placement.Family(folder,
            // The popover itself: with a search field over the list, Flux's marker is on the list inside it.
            new Placement.Side(fluxUrl, Held("data-flux-"), "[popover]", kind.Trigger),
            // The page as Flux's loads, not one written with rows already picked: its trigger is another height.
            new Placement.Side(Repo.FileUrl(Path.Combine(output, "rask", $"{kind.Slug}.html")), Held("data-ui-"), "[popover]", kind.Trigger, Shown),
            kind.Edges)
        {
            Refresh = arg.Flag("refresh"),
            All = arg.Flag("all"),
            Width = 260,
        }) : 0;
        await browser.CloseAsync();

        var limit = arg.Flag("all") ? int.MaxValue : 12;
        var failures = 0;
        foreach (var scheme in new[] { "light", "dark" })
        {
            var theirsList = (flux[scheme] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var mineList = (rask[scheme] as JsonArray ?? []).OfType<JsonObject>().ToList();
            for (var nth = 0; nth < theirsList.Count; nth++)
            {
                var theirs = new Example(theirsList[nth]);
                if (arg.Option("section") is { } only && only != theirs.Section)
                {
                    continue;
                }

                var label = $"{scheme} {(theirs.Section.Length > 0 ? theirs.Section : "(intro)")}#{Js.Num(Js.Number(theirs.Json["ordinal"]))} ({nth})";
                // By position: a Livewire example that answered a click is re-rendered, and loses the mark its
                // ordinal is counted by.
                if (nth >= mineList.Count)
                {
                    Console.WriteLine($"MISSING {label}");
                    failures++;
                    continue;
                }

                var mine = new Example(mineList[nth]);
                var diffs = new List<string>();
                if (!Js.Truthy(theirs.Json["open"]))
                {
                    diffs.Add("Flux did not open");
                }

                if (!Js.Truthy(mine.Json["open"]))
                {
                    diffs.Add("Rask did not open");
                }

                CompareTree(kind, theirs, theirs.Nodes[0], mine, mine.Nodes[0], kind.Name, diffs);
                foreach (var state in new[] { "hover", "pressed" })
                {
                    var x = theirs.Json["rows"]?[state] as JsonObject ?? [];
                    var y = mine.Json["rows"]?[state] as JsonObject ?? [];
                    foreach (var key in x.Select(p => p.Key).Concat(y.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
                    {
                        var (xv, yv) = (Js.Str(x[key]), Js.Str(y[key]));
                        if (!Compare.Same(xv, yv, near: false))
                        {
                            diffs.Add($"row:{state} {key}: {xv ?? "(unchanged)"} vs {yv ?? "(unchanged)"}");
                        }
                    }
                }

                failures += diffs.Count > 0 ? 1 : 0;
                Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {label}{(diffs.Count > 0 ? $" — {diffs.Count} difference(s)" : "")}");
                foreach (var d in diffs.Take(limit))
                {
                    Console.WriteLine($"       {d}");
                }

                if (diffs.Count > limit)
                {
                    Console.WriteLine($"       … {diffs.Count - limit} more (--all)");
                }
            }
        }

        Console.WriteLine(failures > 0 ? $"\nflux parity: the open {kind.Name} differs in {failures} example(s)." : $"\nflux parity: the open {kind.Name} matches Flux.");
        if (kind.Edges is not null && view.Sized.Length == 0)
        {
            Console.WriteLine(astray > 0 ? $"flux parity: the {kind.Name}'s list lands elsewhere for {astray} control(s)." : $"flux parity: the {kind.Name}'s list lands where Flux's does.");
        }

        return failures > 0 || astray > 0 ? 1 : 0;
    }

    // ----- The keyboard, on a running app ------------------------------------------------------------------
    // The parity page is static: it has no runtime, so its lists open (the popover is the browser's) and
    // nothing more. `--live <url>` walks the same keys through Flux's examples and through the showcase of a
    // running site (`dotnet run --project src/Rask.Site`, then the URL of the demo's page) and compares what
    // each key left behind: whether the list is open, the active row, the picked rows, what the control says and
    // where focus is. On the running site the row the control names to assistive technology
    // (aria-activedescendant) is held to the active one as well; Flux's is not read, since it goes stale (after
    // Backspace took a pill off, its combobox still names the row picked before). `--record` walks Flux's page
    // alone and prints every step: how a behaviour table is written down before the component is.
    private static async Task<int> WalkBoth(IBrowser browser, OpenKind kind, string fluxUrl, string? live, bool every, int wait, string? only, View view)
    {
        var differs = 0;
        foreach (var asked in kind.Walks.Where(each => only is null || each.Name == only))
        {
            // A phone has no pointer to rest on a row or to take away: those steps are not taken there.
            var walk = view.Phone ? asked with { Keys = [.. asked.Keys.Where(key => !key.StartsWith("hover", StringComparison.Ordinal) && !key.StartsWith("leave", StringComparison.Ordinal))] } : asked;
            var theirs = await WalkOne(browser, kind, fluxUrl, walk, 450, async (wrappers, _) => wrappers[walk.Flux], view);
            var mine = live is not null
                ? await WalkOne(browser, kind, live, walk, wait, async (_, page) =>
                    await (await page.WaitForSelectorAsync(walk.Rask, new PageWaitForSelectorOptions { Timeout = 120000 }))!.EvaluateHandleAsync(kind.LiveRoot), view)
                : theirs;
            var lines = walk.Keys.Select((key, i) => (key, theirs: theirs[i], mine: mine[i])).ToList();
            var off = lines.Where(line => line.theirs != line.mine).ToList();
            differs += off.Count > 0 && walk.Accepted is null ? 1 : 0;
            var verdict = off.Count == 0 ? "ok  " : walk.Accepted is not null ? "DIFF" : "FAIL";
            Console.WriteLine($"{verdict} {walk.Name} — {walk.Keys.Count} steps{(off.Count > 0 ? $", {off.Count} differ" : "")}{(off.Count > 0 && walk.Accepted is not null ? $" (accepted: {walk.Accepted})" : "")}");
            foreach (var line in every ? lines : off)
            {
                Console.WriteLine($"       {line.key,-10} Flux: {line.theirs}");
                if (line.theirs != line.mine)
                {
                    Console.WriteLine($"       {"",-10} Rask: {line.mine}");
                }
            }
        }

        return differs;
    }

    private static async Task<List<string>> WalkOne(IBrowser browser, OpenKind kind, string url, OpenKind.Walk walk, int wait,
        Func<IReadOnlyList<IElementHandle>, IPage, Task<IJSHandle>> find, View view)
    {
        var context = await browser.NewContextAsync(view.Context());
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
        await page.WaitForSelectorAsync(kind.Root, new PageWaitForSelectorOptions { Timeout = 120000 });
        var scope = (await find(await page.QuerySelectorAllAsync("[data-preview-wrapper]"), page)).AsElement()!;
        await scope.Run("el => el.scrollIntoView({ block: 'center' })");
        var trigger = (await scope.QuerySelectorAsync(kind.Trigger))!;
        Task<IReadOnlyList<IElementHandle>> Rows() => scope.QuerySelectorAllAsync(kind.Rows);
        var selectors = new JsonObject
        {
            ["root"] = kind.Root,
            ["trigger"] = kind.Trigger,
            ["popup"] = kind.Popup,
            ["rows"] = kind.AllRows ?? kind.Rows,
            ["search"] = kind.Search,
            ["says"] = kind.Says,
            ["names"] = !url.StartsWith("https://fluxui.dev", StringComparison.Ordinal),
        };
        async Task Press(IElementHandle target)
        {
            // A press is a tap on a phone.
            if (view.Phone)
            {
                await target.TapAsync();
            }
            else
            {
                await target.ClickAsync();
            }
        }

        var states = new List<string>();
        foreach (var key in walk.Keys)
        {
            var split = key.Split(':');
            var (verb, arg) = (split[0], split.Length > 1 ? split[1] : null);
            switch (verb)
            {
                case "focus": await trigger.FocusAsync(); break;
                case "click" when arg is null: await Press(trigger); break;
                case "click": await Press((await Rows())[int.Parse(arg!, CultureInfo.InvariantCulture)]); break;
                case "hover": await (await Rows())[int.Parse(arg!, CultureInfo.InvariantCulture)].HoverAsync(); break;
                case "remove": await Press((await scope.QuerySelectorAllAsync(kind.Remove!))[int.Parse(arg!, CultureInfo.InvariantCulture)]); break;
                case "leave": await page.Mouse.MoveAsync(2, 2); break;
                case "type": await page.Keyboard.TypeAsync(arg ?? "", new KeyboardTypeOptions { Delay = 60 }); break;
                default: await page.Keyboard.PressAsync(verb); break;
            }

            await page.WaitForTimeoutAsync(wait);
            // A headless page left alone draws no frame, and what waits for one — a list placed again under a trigger
            // that grew a line, the row then under the pointer — is read a step late. Two frames are asked for, and
            // what they move is given the time to land.
            await Screen.Frames(page);
            await page.WaitForTimeoutAsync(250);
            states.Add(Js.Str(await scope.Run(ReadState, selectors)) ?? "");
        }

        await context.CloseAsync();
        return states;
    }

    // Runs in the page. What a key left behind, in words both pages can be held to.
    private const string ReadState = """
        function read(scope, selectors) {
          const control = scope.querySelector(selectors.root);
          const trigger = control.querySelector(selectors.trigger);
          const popup = control.querySelector(selectors.popup);
          const words = el => (el.innerText ?? '').trim().replace(/\s+/g, ' ');
          const rows = [...control.querySelectorAll(selectors.rows)].filter(row => getComputedStyle(row).display !== 'none');
          const open = popup.matches(':popover-open');
          const focused = document.activeElement;
          const focus = focused === trigger ? 'trigger'
            : selectors.search && focused?.closest?.(selectors.search) ? 'search'
              : popup.contains(focused) ? 'list'
                : control.contains(focused) ? `${focused.localName} in the control` : 'elsewhere';
          const active = open ? rows.find(row => row.hasAttribute('data-active')) : undefined;
          const named = [...control.querySelectorAll('[aria-activedescendant]')].map(el => document.getElementById(el.getAttribute('aria-activedescendant'))).find(Boolean);
          const says = new Function(`return (${selectors.says})`)()(control, trigger, words);
          return [
            open ? 'open' : 'closed',
            `says "${says}"`,
            `active ${active ? `"${words(active)}"` : '-'}`,
            // Never what Flux's line says: a row named that is not the active one, or one named while the list is shut.
            selectors.names && named !== active ? `NAMES "${words(named ?? active)}"` : '',
            `picked [${rows.filter(row => row.getAttribute('aria-selected') === 'true').map(words).join(', ')}]`,
            open ? `shows ${rows.length}` : '',
            `focus ${focus}`,
          ].filter(Boolean).join(' · ');
        }
        """;

    // Every example of the control on `url`, opened one at a time and measured, in light and in dark. `like`
    // is Flux's measurement when the page is Rask's: each example is given the text its Flux twin inherits from
    // the docs page (parity's inherited rule), and the page the room under its last example that Flux's
    // has, so a list at the foot of it opens downwards on both.
    private static async Task<JsonObject> MeasureOpen(IBrowser browser, OpenKind kind, string url, string shots, View view, JsonObject? like)
    {
        var schemes = new JsonObject();
        var selectors = new JsonObject { ["root"] = kind.Root, ["trigger"] = kind.Trigger, ["popup"] = kind.Popup };
        foreach (var scheme in new[] { "light", "dark" })
        {
            Directory.CreateDirectory(Path.Combine(shots, scheme));
            var options = view.Context();
            options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
            var context = await browser.NewContextAsync(options);
            var page = await context.NewPageAsync();
            var finger = view.Phone ? await context.NewCDPSessionAsync(page) : null;
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            await page.Run("() => document.fonts.ready.then(() => undefined)");
            // Which row is lit is the runtime's to write (data-rask-list), on a page of the kit's as in an app: the
            // parity page is given the hook that does it, and nothing else of the runtime -- or the hooks its kind names.
            if (like is not null)
            {
                // On a phone the overlay hook too: a tap leaves focus on the button it opened (rask-overlay.ts). Not where the
                // page is written with picks made: there `focused` stands in for where Flux's script left focus. And the
                // placement, as in an app: an open list kept inside the viewport (rask-place.ts) — on a phone a list wider than
                // the room left of it is turned to the trigger's other edge, as Flux's is.
                await Runtime.With(page, [.. kind.Runtime ?? ["rask-list.ts"], "rask-place.ts", .. view.Phone && kind.Pick is null ? new[] { "rask-overlay.ts" } : []]);
                if (kind.Ready is not null)
                {
                    await page.WaitForSelectorAsync(kind.Ready, new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
                }
            }

            var examples = new JsonArray();
            var wrappers = await page.QuerySelectorAllAsync("[data-preview-wrapper]");
            if (like is not null)
            {
                await page.Run("() => { document.body.style.paddingBottom = '900px'; }");
            }

            for (var index = 0; index < wrappers.Count; index++)
            {
                var wrapper = wrappers[index];
                // An example whose controls none of them open (disabled, read-only) is no open state on either page.
                if (await wrapper.QuerySelectorAsync(kind.Popup) is null || await wrapper.QuerySelectorAsync(kind.Trigger) is null)
                {
                    continue;
                }

                var twin = like?[scheme] is JsonArray twins && examples.Count < twins.Count ? twins[examples.Count] : null;
                // On a phone the docs page's column is narrower than the parity page's: each example is also given its
                // twin's width and padding, the room the control is laid out in (parity's frame rule).
                if (twin is not null)
                {
                    await wrapper.Run("(w, { inherited, frame }) => Object.assign(w.style, inherited, frame)",
                        new JsonObject { ["inherited"] = twin["inherited"]?.DeepClone(), ["frame"] = view.Sized.Length > 0 ? twin["frame"]?.DeepClone() : new JsonObject() });
                }

                await wrapper.Run("w => w.scrollIntoView({ block: 'center' })");
                var trigger = (await wrapper.QuerySelectorAsync(kind.Trigger))!;
                // On a phone the hover the tap leaves is part of what is measured (a trigger's chevron, a row lit): waited
                // on until it holds (Screen.Tapped), as it lands late on a loaded runner — and then made to hit-test the
                // finger's place again (PageScripts.Looked), on both pages alike: whether the browser looks again by itself
                // once the list is up is down to what lays the page out next, and on Linux the kit's phone field kept its
                // trigger hovered in one run and not in the next (2026-11-04, #1260).
                if (view.Phone)
                {
                    await Screen.Tapped(page, trigger);
                    if (await trigger.BoundingBoxAsync() is { } tapBox)
                    {
                        await page.Run(PageScripts.Looked, new[] { tapBox.X + tapBox.Width / 2, tapBox.Y + tapBox.Height / 2 });
                    }
                }
                else
                {
                    await trigger.ClickAsync();
                }

                await page.WaitForTimeoutAsync(350);
                // A static page has no runtime to pick with: Rask's is written with these rows already picked.
                foreach (var row in like is not null ? [] : kind.Pick ?? [])
                {
                    var picked = (await wrapper.QuerySelectorAllAsync(kind.Rows))[row];
                    if (view.Phone)
                    {
                        await Screen.Tapped(page, picked);
                    }
                    else
                    {
                        await picked.ClickAsync();
                    }

                    await page.WaitForTimeoutAsync(350);
                }

                // A pointer under a hand never rests on one pixel: a hover that a re-render dropped (Livewire answers
                // the click on a backend-search example) is back with the next move. Off the control altogether where
                // rows were picked: Flux's page takes no pointer while a list is open, so nothing on its trigger hovers,
                // and a static page's trigger would. A phone has no pointer: what the tap left is what is measured.
                var at = await trigger.BoundingBoxAsync();
                if (!view.Phone)
                {
                    if (kind.Pick is not null)
                    {
                        await page.Mouse.MoveAsync(2, 2);
                    }
                    else
                    {
                        await page.Mouse.MoveAsync(at!.X + at.Width / 2 + 1, at.Y + at.Height / 2);
                    }
                }

                await page.WaitForTimeoutAsync(100);
                // A static page has no runtime to open a list from an input: the popover is shown as the runtime shows it.
                await wrapper.Run("""
                    (w, popup) => {
                      const el = w.querySelector(popup);
                      if (el && Object.keys(el.dataset).some(k => k.startsWith('ui')) && !el.matches(':popover-open')) el.showPopover();
                    }
                    """, kind.Popup);
                // …none to hear the pointer leave the list after those picks, which is where Flux's cursor goes out…
                if (like is not null && kind.Pick is not null)
                {
                    await wrapper.Run("w => w.querySelectorAll('[data-active]').forEach(row => row.removeAttribute('data-active'))");
                }

                // On a phone Flux's last pick was a finger's TAP on that row: the trigger is no longer under it, and whether
                // the row is lit is the page's own doing (lit on a Mac, not on the Linux runner, #1260). The Rask side is given
                // the same tap on the same row (already picked on this static page, which has no server to pick with), and
                // lights what its runtime lights: neither the trigger's hover nor the row is written by hand. A pick is
                // answered by a render that lays the page out (a pill added), which this static page has not: it is laid out
                // once more away from the finger (PageScripts.Redrawn), so it is read as the browser has Flux's after its pick
                // — the row lit on a Mac, left for the page's corner on Linux (searchable, #1260).
                if (like is not null && kind.Pick is { Count: > 0 } picks && view.Phone)
                {
                    var rows = await wrapper.QuerySelectorAllAsync(kind.Rows);
                    if (picks[^1] < rows.Count)
                    {
                        await Screen.Tapped(page, rows[picks[^1]]);
                        await page.Run(PageScripts.Redrawn);
                    }
                }

                // …and none to hand focus where Flux's script hands it.
                foreach (var selector in like is not null ? kind.Focused ?? [] : [])
                {
                    if (await wrapper.QuerySelectorAsync(selector) is { } target)
                    {
                        await target.FocusAsync();
                        break;
                    }
                }

                var example = (JsonObject)(await wrapper.Run(Collect, new { STYLES = Pressables.Styles, PRESSABLE = Pressables.Pressable, index, selectors }))!;
                var section = Js.Str(example["section"]) is { Length: > 0 } s ? s : "intro";
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, scheme, $"{index.ToString("00", CultureInfo.InvariantCulture)}-{section}.png") });

                var rowsSeen = new JsonObject();
                example["rows"] = rowsSeen;
                // The second option: the first is where the cursor already is.
                var all = await wrapper.QuerySelectorAllAsync(kind.Rows);
                var second = all.Count > 1 ? all[1] : null;
                // On a phone no row is hovered, and a row pressed is a finger held on it, then lifted off the screen
                // (touchCancel), so nothing is picked.
                if (second is not null && finger is not null)
                {
                    var before = (JsonObject)(await second.Run(Look, Pressables.Styles))!;
                    var box = (await second.BoundingBoxAsync())!;
                    await finger.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    {
                        ["type"] = "touchStart",
                        ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = box.X + box.Width / 2, ["y"] = box.Y + box.Height / 2 } },
                    });
                    await page.WaitForTimeoutAsync(200);
                    rowsSeen["pressed"] = Changed(before, (JsonObject)(await second.Run(Look, Pressables.Styles))!);
                    await finger.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchCancel", ["touchPoints"] = Array.Empty<object>() });
                    await page.WaitForTimeoutAsync(100);
                }
                else if (second is not null)
                {
                    var before = (JsonObject)(await second.Run(Look, Pressables.Styles))!;
                    await second.HoverAsync();
                    await page.WaitForTimeoutAsync(150);
                    rowsSeen["hover"] = Changed(before, (JsonObject)(await second.Run(Look, Pressables.Styles))!);
                    await page.Mouse.DownAsync();
                    await page.WaitForTimeoutAsync(100);
                    rowsSeen["pressed"] = Changed(before, (JsonObject)(await second.Run(Look, Pressables.Styles))!);
                    // Released off the row, so nothing is picked.
                    await page.Mouse.MoveAsync(2, 2);
                    await page.Mouse.UpAsync();
                }

                await page.Keyboard.PressAsync("Escape");
                await wrapper.Run("""
                    (w, popup) => {
                      const el = w.querySelector(popup);
                      if (el && Object.keys(el.dataset).some(k => k.startsWith('ui')) && el.matches(':popover-open')) el.hidePopover();
                    }
                    """, kind.Popup);
                await Screen.Press(page, 2, 2, view.Phone);
                await page.WaitForTimeoutAsync(150);
                examples.Add(example);
            }

            schemes[scheme] = examples;
            await context.CloseAsync();
        }

        return schemes;
    }

    private static JsonObject Changed(JsonObject before, JsonObject after)
    {
        var changed = new JsonObject();
        foreach (var (key, value) in after)
        {
            if (Js.Str(value) != Js.Str(before[key]))
            {
                changed[key] = value?.DeepClone();
            }
        }

        return changed;
    }

    // Runs in the page. One node's computed look.
    private const string Look = """
        (el, STYLES) => {
          const computed = getComputedStyle(el);
          return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
        }
        """;

    // Runs in the page. The control of one example, open: every element under its root, measured from the
    // trigger's corner. PageScripts.Collect, from another origin.
    private const string Collect = """
        function collect(wrapper, { STYLES, PRESSABLE, index, selectors }) {
          for (const animation of document.getAnimations()) animation.finish?.();
          const headings = [...document.querySelectorAll('h2[id]')];
          const section = wrapper.dataset.section
            ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
          const ordinal = [...document.querySelectorAll('[data-preview-wrapper]')].slice(0, index)
            .filter(w => (w.getAttribute('data-o-section') ?? '') === section).length;
          wrapper.setAttribute('data-o-section', section);
          const control = wrapper.querySelector(selectors.root);
          const trigger = control.querySelector(selectors.trigger);
          const popup = control.querySelector(selectors.popup);
          const origin = trigger.getBoundingClientRect();
          const keep = name => !/^(class|style|wire:|x-|@|:|data-o$|data-m$|id$)/.test(name);
          const style = (el, pseudo) => {
            const computed = getComputedStyle(el, pseudo);
            return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
          };

          const nodes = [];
          // What is never drawn is not collected, as in Collect, so the 600 nodes are drawn ones on both sides: a
          // <template> (Flux keeps four in a select's button where the kit keeps two), a <script> (the phone's list of
          // countries), an <input type="hidden"> (a control's one bound field).
          const undrawn = el => el.localName === 'template' || el.localName === 'script' || (el.localName === 'input' && el.type === 'hidden');
          [control, ...control.querySelectorAll('*')].filter(el => !undrawn(el)).slice(0, 600).forEach((el, i) => {
            const id = `${index}-${i}`;
            el.setAttribute('data-o', id);
            const box = el.getBoundingClientRect();
            const node = {
              id, parent: el === control ? null : el.parentElement.getAttribute('data-o'), tag: el.tagName.toLowerCase(),
              attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
              text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
              box: [box.x - origin.x, box.y - origin.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
              style: style(el),
              pressable: !!el.closest(PRESSABLE),
              // …or holds what has it: a pillbox's trigger is ringed while the combobox input in it is focused.
              focused: el.matches(':focus-within'),
            };
            for (const pseudo of ['::before', '::after']) {
              const content = getComputedStyle(el, pseudo).content;
              if (content && content !== 'none' && content !== 'normal') node[pseudo] = { content, ...style(el, pseudo) };
            }

            nodes.push(node);
          });

          const inherited = Object.fromEntries(['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing']
            .map(key => [key, getComputedStyle(wrapper)[key]]));
          const frame = Object.fromEntries(['boxSizing', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft']
            .map(key => [key, getComputedStyle(wrapper)[key]]));
          frame.width = `${wrapper.getBoundingClientRect().width}px`;
          return { index, section, ordinal, open: popup.matches(':popover-open'), inherited, frame, nodes };
        }
        """;

    private static void CompareTree(OpenKind kind, Example theirs, MeasuredNode a, Example mine, MeasuredNode b, string where, List<string> diffs)
    {
        if ((kind.Native.TryGetValue(a.Tag, out var native) ? native : a.Tag) != b.Tag)
        {
            diffs.Add($"{where}: tag <{a.Tag}> vs <{b.Tag}>");
        }

        if (a.Text != b.Text)
        {
            diffs.Add($"{where}: text \"{a.Text}\" vs \"{b.Text}\"");
        }

        static bool Differs(double x, double y) => Math.Abs(x - y) > 0.6;
        static bool Boxless(MeasuredNode n) => n.Box[2] == 0 && n.Box[3] == 0;
        var displayed = !(Boxless(a) && Boxless(b));
        if (Differs(a.Box[2], b.Box[2]) || Differs(a.Box[3], b.Box[3]))
        {
            diffs.Add($"{where}: size {Js.Num(a.Box[2])}x{Js.Num(a.Box[3])} vs {Js.Num(b.Box[2])}x{Js.Num(b.Box[3])}");
        }

        if (displayed && (Differs(a.Box[0], b.Box[0]) || Differs(a.Box[1], b.Box[1])))
        {
            diffs.Add($"{where}: offset from the trigger {Js.Num(a.Box[0])},{Js.Num(a.Box[1])} vs {Js.Num(b.Box[0])},{Js.Num(b.Box[1])}");
        }

        // Flux's script lays the open popup out with an inline `position: absolute` and pixel insets; Rask.Ui
        // anchors it in CSS, where a popover in the top layer is `fixed`. Where it ends up is the offset above.
        // The owner-approved departures (Departed) are let go here as in parity: native feel on chrome, and the
        // accent ring on a field that HAS focus, or holds it — an open list's search or combobox input is focused at rest.
        var focused = Js.Truthy(b.Json["focused"]);
        var letGo = new HashSet<string>([.. a.Has("popover") ? ["position"] : Array.Empty<string>(), .. Departed.From(mine, b), .. focused ? Departed.FocusFrom(b) : []]);
        CompareStyles(a.Styles, b.Styles, where, diffs, letGo, b.Pressable);
        // A pseudo-element inherits what its node departs in (a control's `user-select`), as in parity.
        var departs = new HashSet<string>(Departed.From(mine, b));
        foreach (var pseudo in new[] { "::before", "::after" })
        {
            var (pa, pb) = (a.Pseudo(pseudo), b.Pseudo(pseudo));
            if ((pa is null) != (pb is null))
            {
                diffs.Add($"{where}{pseudo}: {(pa is not null ? "only in Flux" : "only in Rask")}");
            }
            else if (pa is not null)
            {
                CompareStyles(pa, pb!, $"{where}{pseudo}", diffs, departs, b.Pressable);
            }
        }

        // A <template> holds what Flux's script clones from; it draws nothing and Rask.Ui has no use for one.
        var ca = theirs.ChildrenOf(a).Where(c => c.Tag != "template").ToList();
        var cb = mine.ChildrenOf(b).Where(c => c.Tag != "template").ToList();
        if (ca.Count != cb.Count)
        {
            diffs.Add($"{where}: children <{string.Join(' ', ca.Select(c => c.Tag))}> vs <{string.Join(' ', cb.Select(c => c.Tag))}>");
            return;
        }

        for (var i = 0; i < ca.Count; i++)
        {
            CompareTree(kind, theirs, ca[i], mine, cb[i], $"{where} > {ca[i].Tag}[{i}]", diffs);
        }
    }

    // `pressable`: the kit's node shows the hand by the owner-approved departure (Pressables.CursorDeparted).
    private static void CompareStyles(JsonObject x, JsonObject y, string where, List<string> diffs, ISet<string> letGo, string pressable)
    {
        foreach (var key in Pressables.Styles)
        {
            var (xv, yv) = (Js.Str(x[key]), Js.Str(y[key]));
            if (Ignored.Contains(key) || letGo.Contains(key) || Compare.Undrawn(key, k => Js.Str(x[k]), k => Js.Str(y[k])) || Compare.Same(xv, yv, near: false))
            {
                continue;
            }

            if (key == "cursor" && Pressables.CursorDeparted(xv, yv, pressable))
            {
                continue;
            }

            diffs.Add($"{where}: {key}: {xv ?? "undefined"} vs {yv ?? "undefined"}");
        }
    }
}

/// <summary>A control that opens a list, as <see cref="OpenList" /> is handed it.</summary>
/// <param name="Slug">Flux's page, and the Rask parity page of the same name.</param>
/// <param name="Name">What the verdict calls it ("select").</param>
/// <param name="Root">Selector of the control's root, Flux's marker and Rask's.</param>
/// <param name="Trigger">Selector of what a click opens it from, inside the root.</param>
/// <param name="Popup">Selector of the popover, inside the root.</param>
/// <param name="Rows">Selector of an option row.</param>
/// <param name="Native">Flux's custom elements and the native element Rask.Ui writes in their place.</param>
/// <param name="Says">Runs in the page: <c>(control, trigger, words) =&gt; …</c>, what the control shows as its answer.</param>
/// <param name="LiveRoot">Runs in the page: the element a walk's <c>rask</c> selector found =&gt; the example around its control.</param>
/// <param name="Walks">The <c>--live</c> walks.</param>
public sealed record OpenKind(string Slug, string Name, string Root, string Trigger, string Popup, string Rows,
    IReadOnlyDictionary<string, string> Native, string Says, string LiveRoot, IReadOnlyList<OpenKind.Walk> Walks)
{
    /// <summary>The Rask parity page, when the open state has one of its own (pillbox-picked).</summary>
    public string? RaskPage { get; init; }

    /// <summary>Selectors, the first that matches being what Flux's script has focused once the list is open.</summary>
    public IReadOnlyList<string>? Focused { get; init; }

    /// <summary>Rows clicked on Flux's page once the list is open; the Rask page is written with them picked.</summary>
    public IReadOnlyList<int>? Pick { get; init; }

    /// <summary><see cref="Rows" /> and the create row.</summary>
    public string? AllRows { get; init; }

    /// <summary>Selector of a search field inside the popup, when there is one.</summary>
    public string? Search { get; init; }

    /// <summary>Selector of what a <c>remove:&lt;n&gt;</c> step presses (a pill's ×).</summary>
    public string? Remove { get; init; }

    /// <summary>
    ///     Which controls are opened again with the control HELD at the viewport's edges, by their place among the page's
    ///     controls that have a popup: where the list lands with no room (<see cref="Placement" />).
    /// </summary>
    public IReadOnlyList<Placement.Host>? Edges { get; init; }

    /// <summary>
    ///     Where the Rask page is served from, when its list is drawn by a file it fetches (the phone's engine) and a
    ///     file:// page cannot fetch one; the page FluxParityPages wrote otherwise.
    /// </summary>
    public string? RaskUrl { get; init; }

    /// <summary>The runtime's hook modules the Rask page is given (<see cref="Flux.Runtime" />); rask-list.ts unless given.</summary>
    public IReadOnlyList<string>? Runtime { get; init; }

    /// <summary>A selector the page matches once the runtime has run.</summary>
    public string? Ready { get; init; }

    /// <summary>One walk: Flux's example by index, the showcase's control by selector, the steps, and why a difference is accepted.</summary>
    public sealed record Walk(string Name, int Flux, string Rask, IReadOnlyList<string> Keys, string? Accepted = null);
}
