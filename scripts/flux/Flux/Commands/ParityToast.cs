using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Toast to Flux's toast AS SHOWN — the half of fluxui.dev/components/toast that <c>parity</c> cannot see,
///     because that page loads as ten buttons and the toast only exists once one is pressed.
/// </summary>
/// <remarks>
///     Flux's side: each scenario below raises its toast on the live page the way the page's own buttons do (the
///     <c>toast-show</c> / <c>toast-group-show</c> events they dispatch, and the documented <c>Flux.toast()</c>), waits for
///     it to settle, and hands the toast's host to the SAME measuring code as every other component (<see cref="Measure" />).
///     The result is written as the measurements of a page named <c>toast-shown</c>. Rask's side: a shown toast is plain
///     markup, so ToastShownParity writes each scenario already drawn. Then <c>parity</c> — unchanged, the same comparison
///     as every component — compares the two, and this command adds the one thing a subtree comparison leaves out: where
///     on the screen each toast is.
///     <para>
///         What is put right on Flux's measurement before it is compared, each in the open (see <see cref="Settle" />):
///         Flux's custom elements pair with the native element Rask writes — ui-toast, ui-toast-group and ui-close are
///         all &lt;div&gt;, and the two hosts are marked as <c>data-flux-*</c> parts so they pair by marker; Flux clones
///         one template for every toast and hides what this one does not use (three of four icons, an empty heading), and
///         Rask renders only what shows, so what is not displayed is left out; a group is compared front to back — Flux
///         appends each new toast, Rask writes the newest first, because CSS can only anchor an element to one earlier in
///         the tree; in an OPEN stack Flux's script moves each toast by a pixel offset it measured (translateY), and Rask's
///         stylesheet places it by anchor with no offset. Both end in the same place, which the box and the screen check
///         state; the offset itself is not compared. The same goes for the paint order Flux gives an always-expanded
///         stack, where nothing overlaps.
///     </para>
///     <para>Last, the countdown (<see cref="CompareCountdowns" />): what holds a toast and what it does once let go, timed on both.</para>
///     <para>
///         On a phone (<c>--view 390x844</c>) a finger does what the pointer does on a desktop: on Flux's page a TAP on
///         the stack opens it and holds every countdown, and a tap elsewhere lets it go (measured 2026-10-27: the stack
///         stayed open and both toasts stayed there 5 s after the tap, and went once a tap landed elsewhere).
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux parity-toast [--view 390x844]                           # measure Flux, compare
///     flux parity-toast --cached --all                             # reuse Flux's measurement
///     </code>
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("parity-toast", "the shown toast, where it is on screen and its countdown, against Flux's")]
public sealed class ParityToast : IFluxCommand
{
    private const string ToastUrl = "https://fluxui.dev/components/toast";

    // Where nothing is: the pointer rests there on a desktop, and a finger taps there to let a stack go.
    private static readonly double[] Away = [5, 450];

    // Each scenario: what the page's buttons would dispatch (`show`), or a documented Flux.toast() call. Every toast is
    // permanent (duration 0) so it is still there when it is measured.
    private static readonly JsonObject Scenarios = Build();

    private const string Single = "ui-toast:not(ui-toast-group ui-toast)";

    private static readonly Dictionary<string, string> Native = new(StringComparer.Ordinal) { ["ui-toast"] = "div", ["ui-toast-group"] = "div", ["ui-close"] = "div" };

    // Runs in the page. Where a toast is in the viewport: its host, and each card front to back.
    private const string OnScreen = """
        ({ host, dialog }) => {
          const box = el => { const r = el.getBoundingClientRect(); return [r.x, r.y, r.width, r.height].map(v => Math.round(v * 10) / 10); };
          const el = document.querySelector(host);
          const cards = [...el.querySelectorAll(`:scope > ${dialog}`)].map(d => ({
            text: d.textContent.trim().replace(/\s+/g, ' ').slice(0, 24), box: box(d.firstElementChild), shown: getComputedStyle(d).opacity !== '0', atomic: d.getAttribute('aria-atomic'), variant: d.getAttribute('data-variant'),
          }));
          return { host: box(el), role: el.getAttribute('role'), popover: el.getAttribute('popover'), open: el.matches(':popover-open'), cards };
        }
        """;

    // Runs in the page, on either side: when each toast showed and went, on the page's own clock.
    private const string Clock = """
        () => {
          window.__toasts = { shown: {}, gone: {} };
          window.__mark = () => Math.round(performance.now());
          window.__went = name => { window.__toasts.gone[name] ??= Math.round(performance.now()); };
        }
        """;

    // Runs in the page: the front card of Flux's stack, 40px in.
    private const string FluxFront = """
        () => {
          const box = [...document.querySelectorAll('ui-toast-group > [data-flux-toast-dialog]')].pop().firstElementChild.getBoundingClientRect();
          return [box.x + 40, box.y + box.height / 2];
        }
        """;

    private sealed record Countdown(double Held, bool Opaque, double[] Late, string Order, double Focused = double.NaN);

    private readonly JsonObject screens = new()
    {
        ["flux"] = new JsonObject { ["light"] = new JsonObject(), ["dark"] = new JsonObject() },
        ["rask"] = new JsonObject { ["light"] = new JsonObject(), ["dark"] = new JsonObject() },
    };

    private View view = null!;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        view = View.Of(arg);
        var fluxDir = Repo.Parity("flux", "toast-shown");
        var raskPage = Repo.Parity("rask", "toast-shown.html");
        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        // A viewport of its own is a measurement of its own, kept beside the desktop's (as parity keeps it).
        var measured = Path.Combine(fluxDir, $"measurements{view.Sized}.json");
        var where = Path.Combine(fluxDir, $"screens{view.Sized}.json");
        if (arg.Flag("cached") && File.Exists(measured))
        {
            foreach (var (key, value) in (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(where))!)
            {
                screens["flux"]![key] = value?.DeepClone();
            }
        }
        else
        {
            var flux = await MeasureFlux(fluxDir);
            Directory.CreateDirectory(fluxDir);
            await File.WriteAllTextAsync(measured, Js.Stringify(flux));
            await File.WriteAllTextAsync(where, Js.Stringify(screens["flux"]));
        }

        await MeasureRaskScreens(raskPage);
        var compared = await new Parity().Run(["toast-shown", .. args.Where(a => a != "--cached")]);
        var moved = CompareScreens();
        var mistimed = await CompareCountdowns();
        return compared != 0 || moved > 0 || mistimed > 0 ? 1 : 0;
    }

    private static JsonObject Build()
    {
        JsonNode Saved() => new JsonObject { ["slots"] = new JsonObject { ["text"] = "Your changes have been saved." } };
        JsonNode Four() => JsonNode.Parse("""
            [
              { "slots": { "text": "First, a plain one." } },
              { "slots": { "heading": "Second", "text": "With a heading, so it is taller." }, "dataset": { "variant": "success" } },
              { "slots": { "text": "Third, plain again." } },
              { "slots": { "text": "Fourth." } }
            ]
            """)!;
        JsonNode Corner(string position)
        {
            var toast = (JsonObject)Saved();
            toast["dataset"] = new JsonObject { ["position"] = position };
            return new JsonObject { ["show"] = new JsonArray(toast) };
        }

        JsonNode Shown(string json) => new JsonObject { ["show"] = new JsonArray(JsonNode.Parse(json)) };
        return new JsonObject
        {
            ["basic"] = new JsonObject { ["show"] = new JsonArray(Saved()) },
            ["heading"] = Shown("""{ "slots": { "heading": "Changes saved", "text": "You can always update this in your settings." } }"""),
            ["success"] = Shown("""{ "slots": { "heading": "Post created", "text": "The post has been created successfully." }, "dataset": { "variant": "success" } }"""),
            ["warning"] = Shown("""{ "slots": { "heading": "Unsaved changes", "text": "Your post has unsaved changes." }, "dataset": { "variant": "warning" } }"""),
            ["danger"] = Shown("""{ "slots": { "heading": "Something went wrong", "text": "Your changes have not been saved." }, "dataset": { "variant": "danger" } }"""),
            ["invert"] = Shown("""{ "slots": { "heading": "Changes saved", "text": "Your updates are now live." }, "dataset": { "invert": "" } }"""),
            ["invert-success"] = Shown("""{ "slots": { "heading": "Changes saved", "text": "Your updates are now live." }, "dataset": { "invert": "", "variant": "success" } }"""),
            ["action"] = JsonNode.Parse("""{ "toast": { "heading": "Changes saved", "text": "Your updates are now live.", "variant": "success", "action": { "label": "Undo", "event": "undo-changes" } } }"""),
            ["action-link"] = JsonNode.Parse("""{ "toast": { "text": "Invoice created.", "action": { "label": "View", "href": "/components/toast" } } }"""),
            ["link"] = JsonNode.Parse("""{ "toast": { "text": "Invoice created.", "variant": "success", "link": { "label": "View invoice", "href": "/components/toast" } } }"""),
            ["top-end"] = Corner("top end"),
            ["top-center"] = Corner("top center"),
            ["top-start"] = Corner("top start"),
            ["bottom-center"] = Corner("bottom center"),
            ["bottom-start"] = Corner("bottom start"),
            ["group"] = new JsonObject { ["group"] = true, ["show"] = new JsonArray(Saved()) },
            ["group-deck"] = new JsonObject { ["group"] = true, ["show"] = Four() },
            ["group-open"] = new JsonObject { ["group"] = true, ["show"] = Four(), ["hover"] = true, ["open"] = true },
            ["group-expanded"] = new JsonObject { ["group"] = true, ["show"] = Four(), ["expanded"] = true, ["open"] = true },
        };
    }

    private static bool Is(JsonNode? scenario, string key) => Js.Truthy(scenario?[key]);

    // A tap, and the mouse a phone's browser makes of it left where the finger was. On a Mac Chromium leaves it there by
    // itself. On Linux it hovers what was tapped and then, a dozen milliseconds on, takes that mouse back to where the
    // page's own pointer last was (measured 2026-10-30 in Playwright's 1.63 image: mouseleave off the stack, mouseenter
    // on the docs header), so Flux's stack shut as soon as it opened and neither page held its countdown. Moving the
    // page's pointer to the tap first is what keeps it there on both platforms.
    private static async Task Tap(IPage page, double x, double y)
    {
        await page.Mouse.MoveAsync((float)x, (float)y);
        await Screen.Press(page, x, y, true);
    }

    // Over the front toast: the pointer moved there, or a tap.
    private Task Over(IPage page, double[] at) => view.Phone ? Tap(page, at[0], at[1]) : page.Mouse.MoveAsync((float)at[0], (float)at[1]);

    // Off it again: the pointer away, or a tap elsewhere.
    private Task Off(IPage page) => Over(page, Away);

    private static double[] Point(JsonNode? node) => (node as JsonArray ?? []).Select(Js.Number).ToArray();

    // ----- The countdown: what holds it, and what it does afterwards -------------------------------------------------
    //
    // Timed, on both pages, the same way: two toasts in a group (3 s and 4 s), the pointer over the stack one second in
    // and held there for five, then taken away; and one toast with focus on its close button and the pointer elsewhere.
    // Flux's page raises them by its own events. Rask's page (ToastTimedParity) has them drawn, and is given Rask's
    // runtime, which counts them down and presses each one's close button — that press is what is timed, and an app's
    // handler would take the toast off the page in it.
    //
    // `late` is how long after its own remainder a toast went once the pointer had left: the fade, 350 ms on both. A
    // countdown that RESTARTED under the pointer would be late by the second it had already run.
    private async Task<int> CompareCountdowns()
    {
        var browser = await Browsers.Chromium();
        var flux = await FluxCountdown(browser);
        var rask = await RaskCountdown(browser);
        await browser.CloseAsync();

        var checks = new (string Name, Func<Countdown, bool> Passes, Func<Countdown, string> Says)[]
        {
            ("every toast of a hovered stack stays", side => side.Held == 2, side => $"{Js.Num(side.Held)} of 2 there after 5 s under the pointer"),
            ("and none of them fades while it is held", side => side.Opaque, side => side.Opaque ? "all opaque" : "one has faded"),
            ("each resumes its own remainder, then fades", side => side.Late.All(ms => ms >= 250 && ms <= 520), side => $"late by {string.Join(" and ", side.Late.Select(Js.Num))} ms"),
            ("the one with less left goes first", side => side.Order == "First,Second", side => side.Order),
            ("focus inside holds nothing", side => side.Focused >= 2250 && side.Focused <= 2520, side => $"gone {Js.Num(side.Focused)} ms after it showed (2000 + the fade)"),
        };
        var failures = 0;
        Console.WriteLine("\ncountdown (flux | rask)");
        foreach (var (name, passes, says) in checks)
        {
            var ok = passes(flux) && passes(rask);
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name,-44} {says(flux)} | {says(rask)}");
        }

        // Math.max: NaN where either side lost a toast.
        var apart = flux.Late.Select((ms, i) => Math.Abs(ms - rask.Late[i])).Aggregate(double.NegativeInfinity, (max, d) => double.IsNaN(max) || double.IsNaN(d) ? double.NaN : Math.Max(max, d));
        failures += apart <= 120 ? 0 : 1;
        Console.WriteLine($"{(apart <= 120 ? "ok  " : "FAIL")} {"the two pages agree",-44} within {Js.Num(apart)} ms");
        Console.WriteLine(failures > 0 ? $"\nflux parity: the toast's countdown differs in {failures} check(s)." : "\nflux parity: the toast counts down as Flux's does.");
        return failures;
    }

    private static double Gone(JsonNode? toasts, string side, string name) => toasts?[side]?[name] is { } at ? Js.Number(at) : double.NaN;

    private async Task<Countdown> Hold(IPage page, double[] front)
    {
        var hover = Js.Number(await page.Run("() => window.__mark()"));
        await Over(page, front);
        await page.WaitForTimeoutAsync(5000);
        const string stacked = "ui-toast-group > [data-flux-toast-dialog], [data-ui-toast-group] > [data-ui-toast-dialog]";
        var held = Js.Number(await page.Run("stacked => document.querySelectorAll(stacked).length", stacked));
        // Held is not enough: a toast whose fade ran on would be there and not be seen.
        var opaque = Js.Truthy(await page.Run("""
            stacked => [...document.querySelectorAll(stacked)]
              .every(dialog => [dialog, dialog.firstElementChild].every(el => getComputedStyle(el).opacity === '1'))
            """, stacked));
        await Off(page);
        var leave = Js.Number(await page.Run("() => window.__mark()"));
        await page.WaitForTimeoutAsync(6000);
        var toasts = await page.Run("() => window.__toasts");
        var lasts = new Dictionary<string, double> { ["First"] = 3000, ["Second"] = 4000 };
        var late = lasts.Select(last => Gone(toasts, "gone", last.Key) - leave - (last.Value - (hover - Gone(toasts, "shown", last.Key)))).ToArray();
        // Sorted by when each went, as Array.sort orders two: swapped only when the second went strictly first.
        var order = Gone(toasts, "gone", "Second") < Gone(toasts, "gone", "First") ? "Second,First" : "First,Second";
        return new Countdown(held, opaque, late, order);
    }

    private async Task<Countdown> FluxCountdown(IBrowser browser)
    {
        var context = await browser.NewContextAsync(view.Context());
        var page = await context.NewPageAsync();
        Task Raise(string name, int duration, string type) => page.Run("""
            ({ name, duration, event }) => {
              window.__toasts.shown[name] = window.__mark();
              document.body.dispatchEvent(new CustomEvent(event, { detail: { duration, slots: { text: name } }, bubbles: true }));
            }
            """, new { name, duration, @event = type });
        async Task Load()
        {
            await page.GotoAsync(ToastUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            if (!view.Phone)
            {
                await page.Mouse.MoveAsync((float)Away[0], (float)Away[1]);
            }

            await page.Run(Clock);
            // Flux takes a toast out of its host when it has gone.
            await page.Run("""
                () => new MutationObserver(records => {
                  for (const record of records) for (const node of record.removedNodes) if (node.nodeType === 1 && node.matches('[data-flux-toast-dialog]')) window.__went(node.textContent.trim());
                }).observe(document.body, { childList: true, subtree: true })
                """);
        }

        await Load();
        await Raise("First", 3000, "toast-group-show");
        await page.WaitForTimeoutAsync(500);
        await Raise("Second", 4000, "toast-group-show");
        await page.WaitForTimeoutAsync(500);
        var front = Point(await page.Run(FluxFront));
        var stack = await Hold(page, front);

        await Load();
        await Raise("Focus", 2000, "toast-show");
        await page.WaitForTimeoutAsync(400);
        await page.Run("() => [...document.querySelectorAll('[data-flux-toast-dialog]')].find(d => d.textContent.trim() === 'Focus').querySelector('button').focus()");
        await page.WaitForTimeoutAsync(3500);
        var toasts = await page.Run("() => window.__toasts");
        await context.CloseAsync();
        return stack with { Focused = Gone(toasts, "gone", "Focus") - Gone(toasts, "shown", "Focus") };
    }

    private async Task<Countdown> RaskCountdown(IBrowser browser)
    {
        var context = await browser.NewContextAsync(view.Context());
        var page = await context.NewPageAsync();
        var timed = Repo.Parity("rask", "toast-timed.html");
        var hooks = Runtime.Bundle("rask-dom.ts", "rask-hooks.ts");
        // The runtime starts each countdown as it arrives, so that is when a toast "showed"; its press on the close
        // button is when it went.
        async Task Load()
        {
            await page.GotoAsync(Repo.FileUrl(timed), new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            if (!view.Phone)
            {
                await page.Mouse.MoveAsync((float)Away[0], (float)Away[1]);
            }

            await page.Run(Clock);
            await page.Run("""
                () => document.addEventListener('click', e => {
                  const dialog = e.target.closest?.('[data-rask-dismiss]')?.closest('[data-ui-toast-dialog]');
                  if (dialog) { window.__went(dialog.textContent.trim()); dialog.remove(); }
                }, true)
                """);
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = hooks });
            await page.Run("""
                () => {
                  for (const dialog of document.querySelectorAll('[data-ui-toast-dialog]')) window.__toasts.shown[dialog.textContent.trim()] = window.__mark();
                }
                """);
        }

        await Load();
        var front = Point(await page.Run("""
            () => {
              const box = document.querySelector('[data-ui-toast-group] > [data-ui-toast-dialog]').firstElementChild.getBoundingClientRect();
              return [box.x + 40, box.y + box.height / 2];
            }
            """));
        await page.WaitForTimeoutAsync(1000);
        var stack = await Hold(page, front);

        await Load();
        await page.WaitForTimeoutAsync(400);
        await page.Run("() => document.querySelector('[data-ui-toast] [data-rask-dismiss]').focus()");
        await page.WaitForTimeoutAsync(3500);
        var toasts = await page.Run("() => window.__toasts");
        await context.CloseAsync();
        return stack with { Focused = Gone(toasts, "gone", "Focus") - Gone(toasts, "shown", "Focus") };
    }

    private async Task<JsonObject> MeasureFlux(string fluxDir)
    {
        var browser = await Browsers.Chromium();
        var result = new JsonObject { ["light"] = new JsonArray(), ["dark"] = new JsonArray() };
        var index = 0;
        foreach (var (name, scenario) in Scenarios)
        {
            var schemes = await Measure.MeasurePage(browser, ToastUrl, Path.Combine(fluxDir, name + view.Sized), (page, scheme) => Raise(page, scheme, name, scenario!), [view.Width, view.Height]);
            foreach (var scheme in new[] { "light", "dark" })
            {
                if ((schemes[scheme] as JsonArray)?.FirstOrDefault() is not JsonObject example)
                {
                    throw new FluxExit(1, $"flux parity: {name} raised no toast on Flux's page ({scheme})");
                }

                var settled = Settle(example, scenario!);
                settled["index"] = index;
                settled["section"] = name;
                settled["ordinal"] = 0;
                ((JsonArray)result[scheme]!).Add(settled);
            }

            Console.WriteLine($"measured {name}");
            index++;
        }

        await browser.CloseAsync();
        return result;
    }

    // Runs before the measurement: raises the scenario's toast and makes its host the one thing measured.
    private async Task Raise(IPage page, string scheme, string name, JsonNode scenario)
    {
        var group = Is(scenario, "group");
        var host = group ? "ui-toast-group" : Single;
        if (Is(scenario, "expanded"))
        {
            // `expanded` is read when the group connects, so the page is given a group that has it from the start.
            await page.Run("""
                () => {
                  const group = document.querySelector('ui-toast-group');
                  const expanded = group.cloneNode(true);
                  expanded.setAttribute('expanded', '');
                  group.replaceWith(expanded);
                }
                """);
            await page.WaitForTimeoutAsync(300);
        }

        foreach (var detail in scenario["show"] as JsonArray ?? [])
        {
            await page.Run("({ detail, event }) => document.body.dispatchEvent(new CustomEvent(event, { detail: { duration: 0, ...detail }, bubbles: true }))",
                new JsonObject { ["detail"] = detail!.DeepClone(), ["event"] = group ? "toast-group-show" : "toast-show" });
            await page.WaitForTimeoutAsync(300);
        }

        if (scenario["toast"] is { } toast)
        {
            await page.Run("toast => window.Flux.toast({ duration: 0, ...toast })", toast.DeepClone());
        }

        if (!view.Phone)
        {
            await page.Mouse.MoveAsync((float)Away[0], (float)Away[1]);
        }

        await page.WaitForTimeoutAsync(900);
        if (Is(scenario, "hover"))
        {
            var front = Point(await page.Run(FluxFront));
            var card = await page.EvaluateHandleAsync("([x, y]) => document.elementFromPoint(x, y)", front);
            await Over(page, front);
            // A finger's hover comes with the mouse events the browser makes of the tap: at once on a Mac, a while later
            // on a loaded Linux runner, where the stack was measured before it opened (its back cards still scaled down).
            // Waited on (PageScripts.HoverSettled); then the 900 ms its opening takes.
            await card.Run(PageScripts.HoverSettled);
            await page.WaitForTimeoutAsync(900);
        }

        screens["flux"]![scheme]![name] = await page.Run(OnScreen, new { host, dialog = "[data-flux-toast-dialog]" });
        await page.Run("""
            ({ host, name, mark }) => {
              for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) wrapper.removeAttribute('data-preview-wrapper');
              const el = document.querySelector(host);
              el.setAttribute('data-preview-wrapper', '');
              el.setAttribute('data-section', name);
              el.setAttribute(mark, '');
            }
            """, new { host, name, mark = group ? "data-flux-toast-group" : "data-flux-toast" });
    }

    // Flux's measurement, with the differences the header names put right.
    private static JsonObject Settle(JsonObject example, JsonNode scenario)
    {
        var nodes = (example["nodes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var byParent = nodes.GroupBy(node => Js.Str(node["parent"]) ?? "\0null", StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var kept = new JsonArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonObject node, int depth, int back)
        {
            var children = byParent.GetValueOrDefault(Js.Str(node["id"]) ?? "") ?? [];
            var style = node["style"] as JsonObject ?? [];
            var box = (node["box"] as JsonArray ?? []).Select(Js.Number).ToArray();
            var leftover = Js.Str(style["display"]) == "none"
                || (Js.Str(node["tag"]) == "div" && !Js.Truthy(node["text"]) && children.Count == 0 && box.Length > 3 && box[3] == 0);
            if (leftover)
            {
                return;
            }

            var copy = (JsonObject)node.DeepClone();
            var tag = Js.Str(node["tag"]) ?? "";
            copy["tag"] = Native.GetValueOrDefault(tag, tag);
            var placed = (JsonObject)style.DeepClone();
            foreach (var (key, value) in Placed(depth, back, scenario))
            {
                placed[key] = value;
            }

            copy["style"] = placed;
            kept.Add(copy);
            ids.Add(Js.Str(node["id"]) ?? "");
            var ordered = depth == 0 && Is(scenario, "group") ? Enumerable.Reverse(children).ToList() : children;
            for (var i = 0; i < ordered.Count; i++)
            {
                Walk(ordered[i], depth + 1, i);
            }
        }

        Walk(nodes[0], 0, 0);
        var settled = (JsonObject)example.DeepClone();
        settled["nodes"] = kept;
        settled["states"] = new JsonArray([.. (example["states"] as JsonArray ?? []).Where(state => ids.Contains(Js.Str(state?["node"]) ?? "")).Select(state => state!.DeepClone())]);
        return settled;
    }

    // A toast of an open stack: the offset and paint order Flux's script wrote, which Rask's anchors replace.
    private static IEnumerable<(string Key, string Value)> Placed(int depth, int back, JsonNode scenario)
    {
        if (!Is(scenario, "open") || depth != 1)
        {
            yield break;
        }

        yield return ("transform", "matrix(1, 0, 0, 1, 0, 0)");
        if (Is(scenario, "expanded"))
        {
            yield return ("zIndex", Js.Num(5000 - back));
        }
    }

    private async Task MeasureRaskScreens(string raskPage)
    {
        var browser = await Browsers.Chromium();
        foreach (var scheme in new[] { "light", "dark" })
        {
            var options = view.Context();
            options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
            var context = await browser.NewContextAsync(options);
            var page = await context.NewPageAsync();
            await page.GotoAsync(Repo.FileUrl(raskPage), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Run("() => document.fonts.ready.then(() => undefined)");
            await page.Run("() => document.getAnimations().filter(a => a.effect?.getComputedTiming().endTime !== Infinity).forEach(a => a.finish())");
            foreach (var (name, _) in Scenarios)
            {
                screens["rask"]![scheme]![name] = await page.Run(OnScreen, new { host = $"[data-section=\"{name}\"] :is([data-ui-toast], [data-ui-toast-group])", dialog = "[data-ui-toast-dialog]" });
            }

            await context.CloseAsync();
        }

        await browser.CloseAsync();
    }

    // The screen check: the same toast in the same place, announced the same way.
    private int CompareScreens()
    {
        var failures = 0;
        static bool Near(JsonNode? a, JsonNode? b)
        {
            var (x, y) = (Point(a), Point(b));
            return x.Select((v, i) => i < y.Length && Math.Abs(v - y[i]) <= 0.6).All(ok => ok);
        }

        foreach (var scheme in new[] { "light", "dark" })
        {
            foreach (var (name, scenario) in Scenarios)
            {
                var theirs = (JsonObject)screens["flux"]![scheme]![name]!;
                var mine = (JsonObject)screens["rask"]![scheme]![name]!;
                var diffs = new List<string>();
                var flux = (theirs["cards"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                var cards = Is(scenario, "group") ? Enumerable.Reverse(flux).ToList() : flux;
                var ours = (mine["cards"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                if (!Near(theirs["host"], mine["host"]))
                {
                    diffs.Add($"host at {JsString.Of(theirs, "host")} vs {JsString.Of(mine, "host")}");
                }

                foreach (var key in new[] { "role", "popover", "open" })
                {
                    if (JsString.Json(theirs, key) != JsString.Json(mine, key))
                    {
                        diffs.Add($"{key}: {JsString.Of(theirs, key)} vs {JsString.Of(mine, key)}");
                    }
                }

                if (cards.Count != ours.Count)
                {
                    diffs.Add($"toasts: {cards.Count} vs {ours.Count}");
                }

                for (var i = 0; i < cards.Count && i < ours.Count; i++)
                {
                    var (card, twin) = (cards[i], ours[i]);
                    var text = JsString.Of(card, "text");
                    var shown = Js.Truthy(card["shown"]);
                    if (shown != Js.Truthy(twin["shown"]))
                    {
                        diffs.Add($"\"{text}\" shown: {JsString.Of(card, "shown")} vs {JsString.Of(twin, "shown")}");
                    }

                    if (shown && !Near(card["box"], twin["box"]))
                    {
                        diffs.Add($"\"{text}\" at {JsString.Of(card, "box")} vs {JsString.Of(twin, "box")}");
                    }

                    foreach (var key in new[] { "atomic", "variant" })
                    {
                        if (JsString.Json(card, key) != JsString.Json(twin, key))
                        {
                            diffs.Add($"\"{text}\" {key}: {JsString.Of(card, key)} vs {JsString.Of(twin, key)}");
                        }
                    }
                }

                failures += diffs.Count > 0 ? 1 : 0;
                var at = string.Join(' ', cards.Where(c => Js.Truthy(c["shown"])).Select(c => $"[{JsString.Of(c, "box")}]"));
                Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {scheme} {name} on screen{(diffs.Count > 0 ? "" : $": {at}")}");
                foreach (var diff in diffs)
                {
                    Console.WriteLine($"       {diff}");
                }
            }
        }

        Console.WriteLine(failures > 0 ? $"\nflux parity: the shown toast is elsewhere on screen in {failures} case(s)." : "\nflux parity: every shown toast is where Flux puts it.");
        return failures;
    }
}
