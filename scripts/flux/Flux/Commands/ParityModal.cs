using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Modal to Flux UI's modal with the dialog OPEN, which <c>parity</c> cannot see: a modal as loaded is a
///     trigger and a dialog nobody is shown.
/// </summary>
/// <remarks>
///     For each example on fluxui.dev/components/modal and its twin on artifacts/flux-parity/rask/modal.html:
///     <list type="bullet">
///         <item><c>loaded</c> — the page as it arrives, compared the way <c>parity</c> compares it (<see cref="ParityComparer" />);</item>
///         <item>
///             <c>open</c> — the trigger pressed and the dialog settled: the dialog's subtree, where the viewport puts it,
///             its ::backdrop, data-open, the page lock and what holds focus, in light and in dark;
///         </item>
///         <item><c>motion</c> — the transitions that run as it opens and as it closes (property, duration, easing, values);</item>
///         <item>
///             <c>behaviour</c> — Escape, a click outside, a press dragged out of or into the panel, a click on the panel's
///             own padding, the close button, a modal.close inside the content: which events each raises and where focus
///             goes back to.
///         </item>
///     </list>
///     <para>
///         Then the same dialogs with the PAGE owning the state (artifacts/flux-parity/rask/modal-state.html, Rask's
///         wire:model, which Flux's page does not show): the command changes data-rask-modal-open, as a render would, and
///         the dialog that opens is held to the one Flux's trigger opened, and closed every way again.
///     </para>
///     <para>
///         Last, the confirmation as an app writes it, which Flux's page does not show either: a question for a heading, no
///         text under it, two buttons — with the example's min-w-[22rem] and as a BARE modal, without it. The command makes
///         each case on Flux's page (<see cref="Confirm" />) and holds artifacts/flux-parity/rask/modal-confirm.html to it:
///         the open dialog in light and dark, then at 1280 and at 390 wide how wide it is, where the close button sits
///         against its corner and how near the first line of the heading comes to that button. Ui.ConfirmLeave, on the
///         same page, is not Flux's: its close button is held to Flux's corner and its question clear of it.
///     </para>
///     <para>
///         What Flux does in script the kit asks the runtime for (data-rask-modal, data-rask-modal-open, data-rask-lock),
///         so both Rask pages get the runtime's hooks (<see cref="Runtime" />). Flux's page has Flux's script.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux parity-modal [--refresh] [--all]
///     </code>
///     Flux's side is cached in artifacts/flux-parity/flux/modal/open.json and confirm.json; --refresh measures it again.
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("parity-modal", "the modal loaded, open, in motion and closed every way, against Flux's")]
public sealed partial class ParityModal : IFluxCommand
{
    private const string Trigger = "[data-flux-modal-trigger] button, [data-ui-modal-trigger] button";
    private const string Close = "dialog [data-flux-modal-close] button, dialog [data-ui-modal-close] button";
    private const string Hooks = "rask-hooks.ts";
    private static readonly string[] Schemes = ["light", "dark"];
    private static readonly string[] Inherited = ["color", "fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing"];
    private static readonly string[] Layer = ["display", "position", "backgroundColor", "opacity", "backdropFilter", "transform", "transitionProperty", "transitionDuration", "transitionTimingFunction", "transitionBehavior"];

    // parity's comparison, with this page's own parts: the modal, its trigger and its close button.
    private static readonly ParityComparer Comparer = new("modal", false, new HashSet<string>(StringComparer.Ordinal) { "modal", "modal-trigger", "modal-close" });

    // What only a dialog the page opens can be asked: the page changing its mind, and a reader closing a dialog the page
    // still says is open.
    private static readonly JsonObject StateOnly = new()
    {
        ["the page says closed"] = "closed; events: close; focus on nothing (body); page overflow visible",
        ["closed by the reader, then the page says open again"] = "open, data-open; events: none; focus on nothing (body); page overflow hidden",
    };

    // The cases of ModalConfirmParity, in its order. `bare` drops the width Flux's example hands the modal.
    private const string Question = "Biztos elhagyod mentés nélkül az oldalt?";
    private const string Long = "Biztos elhagyod mentés nélkül az oldalt, és eldobod, amit eddig beírtál az űrlapba?";

    private sealed record ConfirmCase(string Name, bool Bare, string Heading);

    private static readonly ConfirmCase[] Confirm =
    [
        new("a bare modal asking a question", true, Question),
        new("a bare modal under a short heading", true, "Delete project?"),
        new("a confirmation asking a question", false, Question),
        new("a bare modal asking a long question", true, Long),
        new("a confirmation asking a long question", false, Long),
    ];

    // Measured in this order; filed in ascending order, as a JavaScript object keyed by width orders its keys.
    private static readonly int[] Widths = [1280, 390];

    // Ui.Confirm's answer field, and the one line its Yes is let go on.
    private const string Answer = "data-rask-confirm-dialog";
    private const string Placeholder = "the focus placeholder leaves by a CSS animation (ui-modal-placeholder) where Flux removes it from script";
    private const string Clear = "the first line is clear of the close button";

    // Runs in the page. An open dialog's width, its close button against its corner, and its heading against that button.
    private const string CornerFacts = """
        (i) => {
          const fix = v => Math.round(v * 10) / 10;
          const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
          const box = dialog.getBoundingClientRect();
          const own = getComputedStyle(dialog);
          const close = [...dialog.querySelectorAll('[data-flux-modal-close] button, [data-ui-modal-close] button')].pop().getBoundingClientRect();
          const range = document.createRange();
          range.selectNodeContents(dialog.querySelector('[data-flux-heading], [data-ui-heading]'));
          const lines = [...range.getClientRects()];
          return {
            'dialog size': `${fix(box.width)}x${fix(box.height)}`,
            'min-width | max-width': `${own.minWidth} | ${own.maxWidth}`,
            'close button': `${fix(close.width)}x${fix(close.height)}, ${fix(close.top - box.top)} from the top edge, ${fix(box.right - close.right)} from the right edge`,
            'heading lines': new Set(lines.map(line => Math.round(line.top))).size,
            'first line ends before the close button': fix(close.left - lines[0].right),
          };
        }
        """;

    // Runs in the page. What an open dialog is that no node of the example's tree states.
    private const string OpenFacts = """
        ({ i, LAYER }) => {
          const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
          const box = dialog.getBoundingClientRect();
          const own = getComputedStyle(dialog);
          const backdrop = getComputedStyle(dialog, '::backdrop');
          const html = getComputedStyle(document.documentElement);
          const active = document.activeElement;
          // A gutter is kept where a scrollbar takes room. Where none does — macOS draws its scrollbars over the page even
          // when asked to show them — Flux's `scrollbar-gutter: stable` holds nothing and the kit does not write it
          // (rask-lock.ts), so there the lock is compared without it, as parity-menu compares it.
          const probe = document.createElement('div');
          probe.style.cssText = 'position:absolute;top:0;width:100px;height:100px;overflow:scroll;visibility:hidden';
          document.body.append(probe);
          const room = probe.offsetWidth - probe.clientWidth;
          probe.remove();
          return {
            'dialog open': dialog.open,
            'dialog :modal (top layer)': dialog.matches(':modal'),
            'dialog data-open': dialog.hasAttribute('data-open'),
            'dialog box in the viewport': [box.x, box.y, box.width, box.height].map(v => Math.round(v * 10) / 10).join(' '),
            'dialog names itself': dialog.getAttribute('data-modal') !== null,
            'dialog transition': `${own.transitionProperty} ${own.transitionDuration} ${own.transitionTimingFunction} ${own.transitionBehavior}`,
            ...Object.fromEntries(LAYER.map(key => [`::backdrop ${key}`, backdrop[key]])),
            'page lock (overflow | pointer-events | scrollbar-gutter)': `${html.overflowY} | ${html.pointerEvents} | ${room ? html.scrollbarGutter : 'no scrollbar takes room'}`,
            'focus rests on': active === document.body ? 'nothing (body)' : dialog.contains(active) ? `<${active.tagName.toLowerCase()}> in the dialog` : 'the page behind',
          };
        }
        """;

    private IBrowser browser = null!;
    private JsonObject flux = null!;
    private int confirmation;
    private int failures;
    private int limit;
    private readonly List<string> accepted = [];

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        limit = arg.Flag("all") ? int.MaxValue : 12;
        var raskPage = Repo.Parity("rask", "modal.html");
        var statePage = Repo.Parity("rask", "modal-state.html");
        var confirmPage = Repo.Parity("rask", "modal-confirm.html");
        foreach (var page in new[] { raskPage, statePage, confirmPage })
        {
            if (!File.Exists(page))
            {
                Console.Error.WriteLine($"flux parity: {page} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
                return 1;
            }
        }

        // A browser that SHOWS its scrollbars, as a reader's does. Headless hides them, and there Flux's
        // `scrollbar-gutter: stable` reserves 15px that nothing was using: its own dialog sits 7.5px off centre and its
        // flyout 15px short of the edge. The kit keeps the gutter only where a scrollbar was taking room (rask-lock.ts),
        // so the two agree where it matters — with the scrollbar there.
        browser = await Browsers.Chromium(new BrowserTypeLaunchOptions { IgnoreDefaultArgs = ["--hide-scrollbars"] });
        var fluxDir = Repo.Parity("flux", "modal");
        var fluxFile = Path.Combine(fluxDir, "open.json");
        if (File.Exists(fluxFile) && !arg.Flag("refresh"))
        {
            flux = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(fluxFile))!;
        }
        else
        {
            flux = await Observe("https://fluxui.dev/components/modal", fluxDir, null);
            Directory.CreateDirectory(fluxDir);
            await File.WriteAllTextAsync(fluxFile, Js.Stringify(flux));
        }

        var loadedLight = Example.Of(flux["loaded"]!["light"]);
        confirmation = loadedLight.FindIndex(example => example.Section == "confirmation");
        var fluxConfirmFile = Path.Combine(fluxDir, "confirm.json");
        JsonObject fluxConfirm;
        if (File.Exists(fluxConfirmFile) && !arg.Flag("refresh"))
        {
            fluxConfirm = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(fluxConfirmFile))!;
        }
        else
        {
            fluxConfirm = await ObserveConfirm("https://fluxui.dev/components/modal", fluxDir, null);
            await File.WriteAllTextAsync(fluxConfirmFile, Js.Stringify(fluxConfirm));
        }

        var rask = await Observe(Repo.FileUrl(raskPage), Repo.Parity("rask", "modal"), flux);
        var state = await ObserveState(Repo.FileUrl(statePage), flux);
        var confirm = await ObserveConfirm(Repo.FileUrl(confirmPage), Repo.Parity("rask", "modal-confirm"), flux);
        var leave = await ObserveLeave(Repo.FileUrl(confirmPage), Repo.Parity("rask", "modal-confirm"));
        var confirmDialog = await ObserveConfirmDialog(Repo.FileUrl(confirmPage), Repo.Parity("rask", "modal-confirm"));
        await browser.CloseAsync();

        string Name(int i) => loadedLight[i].Section is { Length: > 0 } section ? section : "intro";
        foreach (var scheme in Schemes)
        {
            var theirsList = Example.Of(flux["loaded"]![scheme]);
            var mineList = Example.Of(rask["loaded"]![scheme]);
            for (var i = 0; i < theirsList.Count; i++)
            {
                if (i >= mineList.Count)
                {
                    Report($"{scheme} {Name(i)} loaded", ["Rask has no such example"]);
                    continue;
                }

                var notes = new List<string>();
                Report($"{scheme} {Name(i)} loaded", Looked(mineList[i], Comparer.CompareExample(theirsList[i], mineList[i], notes)), notes);

                var a = flux["open"]![scheme]![i]!;
                var b = rask["open"]![scheme]![i]!;
                Report($"{scheme} {Name(i)} open", [.. CompareOpen(a["example"], b["example"]), .. CompareFacts(a["facts"], b["facts"])]);

                var c = state["open"]![scheme]![i]!;
                Report($"{scheme} {Name(i)} open by the page's state", [.. CompareOpen(a["example"], c["example"]), .. CompareFacts(a["facts"], c["facts"])]);
            }
        }

        var fluxMotion = flux["motion"] as JsonArray ?? [];
        for (var i = 0; i < fluxMotion.Count; i++)
        {
            Report($"motion    {Name(i)}", CompareFacts(fluxMotion[i], rask["motion"]?[i]));
        }

        var fluxBehaviour = flux["behaviour"] as JsonArray ?? [];
        for (var i = 0; i < fluxBehaviour.Count; i++)
        {
            Report($"behaviour {Name(i)}", CompareFacts(fluxBehaviour[i], rask["behaviour"]?[i]));
        }

        // A dialog the page opened has no trigger to hand focus back to: every other word of Flux's answer holds.
        static JsonObject Unfocused(JsonNode? facts, JsonNode? keep)
        {
            var result = new JsonObject();
            foreach (var (key, value) in facts as JsonObject ?? [])
            {
                if (keep is null || (keep as JsonObject)!.ContainsKey(key))
                {
                    result[key] = value is null ? null : Focus().Replace(JsString.Of(value), "", 1);
                }
            }

            return result;
        }

        for (var i = 0; i < fluxBehaviour.Count; i++)
        {
            var mine = state["behaviour"]![i];
            Report($"behaviour {Name(i)}, the page's state", [
                .. CompareFacts(Unfocused(fluxBehaviour[i], mine as JsonObject ?? []), Unfocused(mine, null)),
                .. CompareFacts(StateOnly, state["only"]![i]),
            ]);
        }

        for (var k = 0; k < Confirm.Length; k++)
        {
            foreach (var scheme in Schemes)
            {
                var a = fluxConfirm["open"]![scheme]![k]!;
                var b = confirm["open"]![scheme]![k]!;
                Report($"{scheme} {Confirm[k].Name}, open", [.. CompareOpen(a["example"], b["example"]), .. CompareFacts(a["facts"], b["facts"])]);
            }

            foreach (var width in Widths)
            {
                var key = width.ToString(CultureInfo.InvariantCulture);
                Report($"{width}px {Confirm[k].Name}, its corner", CompareCorner(fluxConfirm["corner"]![key]![k] as JsonObject ?? [], confirm["corner"]![key]![k] as JsonObject));
            }
        }

        // The leave dialog is a confirmation: Flux's corner and Flux's width. Its own: the question clear of the button.
        var asConfirmation = Array.FindIndex(Confirm, c => !c.Bare);
        foreach (var width in Widths)
        {
            var key = width.ToString(CultureInfo.InvariantCulture);
            foreach (var (asked, mineNode) in (JsonObject)leave[key]!)
            {
                var theirs = (JsonObject)fluxConfirm["corner"]![key]![asConfirmation]!;
                var mine = (JsonObject)mineNode!;
                Report($"{width}px the leave dialog asking {asked}", CompareCorner(
                    new JsonObject { ["close button"] = theirs["close button"]?.DeepClone(), ["min-width | max-width"] = theirs["min-width | max-width"]?.DeepClone(), [Clear] = true },
                    new JsonObject { ["close button"] = mine["close button"]?.DeepClone(), ["min-width | max-width"] = mine["min-width | max-width"]?.DeepClone(), [Clear] = Js.Number(mine["first line ends before the close button"]) >= 0 }));
            }
        }

        // Ui.Confirm is Flux's confirmation example, opened by the runtime: held to Flux's open one, node for node. Its
        // answer field is not Flux's — Flux has no prompt — and is hidden unless `.Confirm("…", expect:)` asks for one.
        foreach (var scheme in Schemes)
        {
            var theirs = flux["open"]![scheme]![confirmation]!;
            var mine = confirmDialog[scheme]!;
            var example = new Example((JsonObject)mine["example"]!.DeepClone());
            var all = example.Nodes.Count;
            var gone = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in example.Nodes)
            {
                if ((node.Attr(Answer) == "field" && node.Has("hidden")) || (node.Parent is not null && gone.Contains(node.Parent)))
                {
                    gone.Add(node.Id);
                }
            }

            example.Filter(node => !gone.Contains(node.Id));
            if (example.Nodes.Count < all)
            {
                Accept("Ui.Confirm's answer field, hidden unless .Confirm(\"…\", expect:) asks: Flux has no prompt");
            }

            // Flux's delete is a submit button, so it carries the spinner of the request it sends (and its label in a
            // span). Yes sends nothing — the action it lets go shows its own wait — and is never a submit: a layout's form
            // around the dialog would be sent.
            var diffs = CompareOpen(theirs["example"], example.Json).Concat(CompareFacts(theirs["facts"], mine["facts"])).Where(diff =>
            {
                if (!YesSendsNothing().IsMatch(diff))
                {
                    return true;
                }

                Accept("Ui.Confirm's Yes is no submit button, so it has no spinner and its label is its own text");
                return false;
            }).ToList();
            Report($"{scheme} Ui.Confirm, open", diffs);
        }

        foreach (var note in accepted)
        {
            Console.WriteLine($"accepted: {note}");
        }

        Console.WriteLine(failures > 0 ? $"\nflux parity: modal differs in {failures} check(s)." : "\nflux parity: modal matches Flux, loaded and open.");
        return failures > 0 ? 1 : 0;
    }

    private void Accept(string note)
    {
        if (!accepted.Contains(note))
        {
            accepted.Add(note);
        }
    }

    private void Report(string label, List<string> diffs, List<string>? notes = null)
    {
        failures += diffs.Count > 0 ? 1 : 0;
        Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {label}{(diffs.Count > 0 ? $" — {diffs.Count} difference(s)" : "")}");
        foreach (var note in notes ?? [])
        {
            Console.WriteLine($"       ({note})");
        }

        foreach (var d in diffs.Take(limit))
        {
            Console.WriteLine($"       {d}");
        }

        if (diffs.Count > limit)
        {
            Console.WriteLine($"       … {diffs.Count - limit} more (--all)");
        }
    }

    // The focus placeholder: Flux's script takes it away once the dialog has focus; Rask.Ui ships no script, and its
    // stylesheet does it with an animation (ui-modal-placeholder). Each ends with the placeholder gone, which IS compared.
    private List<string> Looked(Example mine, List<string> diffs)
    {
        if (mine.Nodes.Any(node => node.Has("data-ui-focus-placeholder")))
        {
            Accept(Placeholder);
        }

        return diffs;
    }

    // A Rask parity page as an app has it: the runtime's hooks, and — as Flux's page goes on below its last example —
    // more page than the viewport, so that it scrolls and shows the scrollbar Flux's shows.
    private static async Task AsInAnApp(IPage page)
    {
        await Runtime.With(page, Hooks);
        await page.Run("() => { document.body.style.paddingBottom = '100vh'; }");
    }

    // Each Rask example is given the text its Flux twin inherits from the docs page, and the page the runtime.
    private static async Task Dress(JsonObject? like, IPage page, string scheme)
    {
        if (like is null)
        {
            return;
        }

        await page.Run("""
            ({ examples, INHERITED }) => {
              document.querySelectorAll('[data-preview-wrapper]').forEach((wrapper, i) => {
                for (const key of examples[i] ? INHERITED : []) wrapper.style[key] = examples[i].nodes[0].style[key];
              });
            }
            """, new JsonObject { ["examples"] = like["loaded"]![scheme]!.DeepClone(), ["INHERITED"] = Js.ToNode(Inherited) });
        await AsInAnApp(page);
    }

    // Everything this command compares, for one side. `like` is Flux's side, when measuring Rask's.
    private async Task<JsonObject> Observe(string url, string shots, JsonObject? like)
    {
        var loaded = await Measure.MeasurePage(browser, url, Path.Combine(shots, "loaded"), (page, scheme) => Dress(like, page, scheme));
        var count = (loaded["light"] as JsonArray ?? []).Count;
        var open = await MeasureOpen(url, shots, like, count, Press);

        var walkedPage = await Walked(url, like);
        var motion = new JsonArray();
        var behaviour = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            motion.Add(await WatchMotion(walkedPage, i));
            behaviour.Add(await WatchBehaviour(walkedPage, i));
        }

        await walkedPage.Context.CloseAsync();
        return new JsonObject { ["loaded"] = loaded, ["open"] = open, ["motion"] = motion, ["behaviour"] = behaviour };
    }

    // The page whose state opens its modals. What a render does is all the command does: one attribute.
    private async Task<JsonObject> ObserveState(string url, JsonObject like)
    {
        var count = (like["loaded"]!["light"] as JsonArray ?? []).Count;
        var open = await MeasureOpen(url, Repo.Parity("rask", "modal-state"), like, count, (page, i) => Say(page, i, true));

        var walkedPage = await Walked(url, like);
        var behaviour = new JsonArray();
        var only = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var facts = await WatchStateBehaviour(walkedPage, i);
            only.Add(new JsonObject([.. StateOnly.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, facts[p.Key]?.DeepClone()))]));
            behaviour.Add(new JsonObject([.. facts.Where(p => !StateOnly.ContainsKey(p.Key)).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone()))]));
        }

        await walkedPage.Context.CloseAsync();
        return new JsonObject { ["open"] = open, ["behaviour"] = behaviour, ["only"] = only };
    }

    // Every case of Confirm, for one side. On Flux's page each is made from the confirmation example; the Rask page is
    // written with one example per case, and is dressed as that Flux example is.
    private async Task<JsonObject> ObserveConfirm(string url, string shots, JsonObject? like)
    {
        int At(int k) => like is not null ? k : confirmation;
        Task Prepare(int k, IPage page, string scheme) => like is not null
            ? DressAs(page, like["loaded"]![scheme]![confirmation]!)
            : page.Run("""
                ({ i, bare, heading }) => {
                  const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
                  if (bare) dialog.classList.remove(...[...dialog.classList].filter(name => /^min-w-\[22rem\]!?$/.test(name)));
                  dialog.querySelector('[data-flux-heading]').textContent = heading;
                  dialog.querySelector('[data-flux-text]').remove();
                }
                """, new { i = confirmation, name = Confirm[k].Name, bare = Confirm[k].Bare, heading = Confirm[k].Heading });
        var open = new JsonObject { ["light"] = new JsonArray(), ["dark"] = new JsonArray() };
        var corner = new JsonObject();
        foreach (var width in Widths.OrderBy(w => w))
        {
            corner[width.ToString(CultureInfo.InvariantCulture)] = new JsonArray();
        }

        for (var k = 0; k < Confirm.Length; k++)
        {
            var facts = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            var index = k;
            var schemes = await Measure.MeasurePage(browser, url, Path.Combine(shots, $"confirm-{k}"), async (page, scheme) =>
            {
                await Prepare(index, page, scheme);
                await Press(page, At(index));
                facts[scheme] = await page.Run(OpenFacts, new { i = At(index), LAYER = Layer });
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"confirm-{index}-{scheme}.png") });
            });
            foreach (var scheme in Schemes)
            {
                ((JsonArray)open[scheme]!).Add(new JsonObject { ["example"] = schemes[scheme]?[At(k)]?.DeepClone(), ["facts"] = facts.GetValueOrDefault(scheme)?.DeepClone() });
            }

            foreach (var width in Widths)
            {
                var page = await Sized(url, width);
                await Prepare(k, page, "light");
                await Press(page, At(k));
                ((JsonArray)corner[width.ToString(CultureInfo.InvariantCulture)]!).Add(await page.Run(CornerFacts, At(k)));
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"confirm-{k}-{width}.png") });
                await page.Context.CloseAsync();
            }
        }

        return new JsonObject { ["open"] = open, ["corner"] = corner };
    }

    // Ui.ConfirmLeave, the last example of the confirm page: closed in every render and opened in the browser with the
    // asking form's message, which is all this does.
    private async Task<JsonObject> ObserveLeave(string url, string shots)
    {
        var facts = new JsonObject();
        foreach (var width in Widths.OrderBy(w => w))
        {
            facts[width.ToString(CultureInfo.InvariantCulture)] = new JsonObject();
        }

        foreach (var width in Widths)
        {
            foreach (var (asked, question) in new[] { ("a question", Question), ("a long question", Long) })
            {
                var page = await Sized(url, width);
                await DressAs(page, flux["loaded"]!["light"]![confirmation]!);
                await page.Run("""
                    ({ i, question }) => {
                      const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
                      dialog.querySelector('[data-rask-leave="message"]').textContent = question;
                      dialog.showModal();
                    }
                    """, new { i = Confirm.Length, question });
                await Settled(page, Confirm.Length, true);
                facts[width.ToString(CultureInfo.InvariantCulture)]![asked] = await page.Run(CornerFacts, Confirm.Length);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"leave-{width}-{asked.Replace(' ', '-')}.png") });
                await page.Context.CloseAsync();
            }
        }

        return facts;
    }

    // Ui.Confirm, the example after Ui.ConfirmLeave: opened as the runtime opens it, with the text of Flux's own
    // confirmation where the question goes. Flux breaks that text in two, so the break is written as well.
    private async Task<JsonObject> ObserveConfirmDialog(string url, string shots)
    {
        var i = Confirm.Length + 1;
        var facts = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var schemes = await Measure.MeasurePage(browser, url, Path.Combine(shots, "confirm-dialog"), async (page, scheme) =>
        {
            await DressAs(page, flux["loaded"]![scheme]![confirmation]!);
            await page.Run("""
                i => {
                  const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
                  dialog.querySelector('[data-rask-confirm-dialog="message"]').innerHTML = 'You\'re about to delete this project.<br>This action cannot be reversed.';
                  dialog.showModal();
                }
                """, i);
            await Settled(page, i, true);
            facts[scheme] = await page.Run(OpenFacts, new { i, LAYER = Layer });
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"confirm-dialog-{scheme}.png") });
        });

        return new JsonObject([.. Schemes.Select(scheme => new KeyValuePair<string, JsonNode?>(scheme,
            new JsonObject { ["example"] = schemes[scheme]?[i]?.DeepClone(), ["facts"] = facts.GetValueOrDefault(scheme)?.DeepClone() }))]);
    }

    // One light page of a given width, with its fonts in: a phone's is where a modal meets the viewport's edges.
    private async Task<IPage> Sized(string url, int width)
    {
        var options = Screen.Of(width, 900);
        options.ColorScheme = ColorScheme.Light;
        var context = await browser.NewContextAsync(options);
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        await page.Run("() => document.fonts.ready.then(() => undefined)");
        return page;
    }

    // Every example of a Rask page given the text one Flux example inherits from the docs page, and the runtime.
    private static async Task DressAs(IPage page, JsonNode example)
    {
        await page.Run("""
            ({ style, INHERITED }) => {
              for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) for (const key of INHERITED) wrapper.style[key] = style[key];
            }
            """, new JsonObject { ["style"] = example["nodes"]![0]!["style"]!.DeepClone(), ["INHERITED"] = Js.ToNode(Inherited) });
        await AsInAnApp(page);
    }

    // Each example measured with its own dialog open, in both schemes, and what no node of its tree states.
    private async Task<JsonObject> MeasureOpen(string url, string shots, JsonObject? like, int count, Func<IPage, int, Task> show)
    {
        var open = new JsonObject { ["light"] = new JsonArray(), ["dark"] = new JsonArray() };
        for (var i = 0; i < count; i++)
        {
            var facts = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            var index = i;
            var schemes = await Measure.MeasurePage(browser, url, Path.Combine(shots, $"open-{i}"), async (page, scheme) =>
            {
                await Dress(like, page, scheme);
                await show(page, index);
                facts[scheme] = await page.Run(OpenFacts, new { i = index, LAYER = Layer });
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"open-{index}-{scheme}.png") });
            });

            foreach (var scheme in Schemes)
            {
                ((JsonArray)open[scheme]!).Add(new JsonObject { ["example"] = schemes[scheme]?[i]?.DeepClone(), ["facts"] = facts.GetValueOrDefault(scheme)?.DeepClone() });
            }
        }

        return open;
    }

    // One light page to walk with pointer and keyboard.
    private async Task<IPage> Walked(string url, JsonObject? like)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 }, ColorScheme = ColorScheme.Light });
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        if (like is not null)
        {
            await AsInAnApp(page);
        }

        return page;
    }

    private static async Task<IElementHandle> WrapperOf(IPage page, int i) => (await page.QuerySelectorAllAsync("[data-preview-wrapper]"))[i];

    private static async Task Press(IPage page, int i)
    {
        await (await (await WrapperOf(page, i)).QuerySelectorAsync(Trigger))!.ClickAsync();
        await Settled(page, i, true);
    }

    // What a render that changed its mind writes: the attribute, and nothing else.
    private static async Task Say(IPage page, int i, bool open)
    {
        await page.Run("""
            ({ i, open }) => {
              document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog').setAttribute('data-rask-modal-open', String(open));
            }
            """, new { i, open });
        await Settled(page, i, open);
    }

    // Waits on STATE, never on a delay: the dialog open (or closed), no transition left running on it or on its backdrop,
    // and its box the same for three frames. A dialog that never gets there is measured as it is.
    private static async Task Settled(IPage page, int i, bool open)
    {
        try
        {
            await page.WaitForFunctionAsync("""
                (a) => {
                  const { i, open } = JSON.parse(a);
                  const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
                  if (dialog.open !== open) return false;
                  if (document.getAnimations().some(a => a.effect?.target === dialog && a.playState === 'running')) return false;
                  const box = JSON.stringify(dialog.getBoundingClientRect());
                  const state = (window.__settled ??= { box: '', frames: 0 });
                  state.frames = state.box === box ? state.frames + 1 : 0;
                  state.box = box;
                  return state.frames >= 3;
                }
                """, Js.Serialize(new { i, open }), new PageWaitForFunctionOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            // a dialog that never gets there is measured as it is
        }

        // The focus placeholder leaves 150ms after the dialog opens, on both pages.
        if (open)
        {
            try
            {
                await page.WaitForFunctionAsync("i => !document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog :focus')", i, new PageWaitForFunctionOptions { Timeout = 2000 });
            }
            catch (TimeoutException)
            {
                // measured as it is
            }
        }
    }

    // The transitions the dialog and its backdrop start on the way in and on the way out.
    private static async Task<JsonObject> WatchMotion(IPage page, int i)
    {
        var wrapper = await WrapperOf(page, i);
        async Task<JsonArray> Running() => (JsonArray)(await wrapper.Run("""
            w => {
              const dialog = w.querySelector('dialog');
              return document.getAnimations()
                .filter(a => a.effect.target === dialog && a.transitionProperty)
                .map(a => {
                  // A transform as the matrix it is: `translateX(50px)` and `translate(50px)` are one movement.
                  const frames = a.effect.getKeyframes()
                    .map(frame => frame[a.transitionProperty.replace(/-(\w)/g, (_, c) => c.toUpperCase())])
                    .map(value => (a.transitionProperty === 'transform' && value && value !== 'none' ? new DOMMatrixReadOnly(value).toString() : value));
                  const timing = a.effect.getComputedTiming();
                  return [`${a.effect.pseudoElement ?? 'dialog'} ${a.transitionProperty}`, `${timing.duration}ms ${timing.easing} +${timing.delay}ms: ${frames.join(' -> ')}`];
                })
                // A property that starts a transition to the value it already has says nothing about the component.
                .filter(([, value]) => !/: (\S.*) -> \1$/.test(value) && !value.includes('undefined'))
                .sort(([x], [y]) => x.localeCompare(y));
            }
            """))!;

        await (await wrapper.QuerySelectorAsync(Trigger))!.ClickAsync();
        var entering = await Running();
        await Settled(page, i, true);
        await page.Keyboard.PressAsync("Escape");
        var leaving = await Running();
        await Settled(page, i, false);
        var motion = new JsonObject();
        foreach (var (way, entries) in new[] { ("in  ", entering), ("out ", leaving) })
        {
            foreach (var entry in entries.OfType<JsonArray>())
            {
                motion[$"{way}{JsString.Of(entry[0])}"] = entry[1]?.DeepClone();
            }
        }

        return motion;
    }

    // Hears the dialog's own events — cancel, close — and says where things stand after each step.
    private sealed record Heard(IElementHandle Wrapper, Func<Task<string>> State, Func<Task<string>> After);

    private static async Task<Heard> Listen(IPage page, int i)
    {
        var wrapper = await WrapperOf(page, i);
        await wrapper.Run("""
            w => {
              const dialog = w.querySelector('dialog');
              window.__events = [];
              for (const type of ['cancel', 'close']) dialog.addEventListener(type, () => window.__events.push(type));
            }
            """);
        async Task<string> State() => Js.Str(await wrapper.Run("""
            (w, TRIGGER) => {
              const dialog = w.querySelector('dialog');
              const events = window.__events.join(' then ') || 'none';
              window.__events = [];
              const active = document.activeElement;
              const focus = active === w.querySelector(TRIGGER) ? 'the trigger' : active === document.body ? 'nothing (body)' : dialog.contains(active) ? 'the dialog' : 'elsewhere';
              const html = getComputedStyle(document.documentElement);
              return `${dialog.open ? 'open' : 'closed'}${dialog.hasAttribute('data-open') ? ', data-open' : ''}; events: ${events}; focus on ${focus}; page overflow ${html.overflowY}`;
            }
            """, Trigger)) ?? "";
        // After a step that may close it: closed and at rest, or still open a moment later.
        async Task<string> After()
        {
            try
            {
                await page.WaitForFunctionAsync("i => !document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog').open", i, new PageWaitForFunctionOptions { Timeout = 600 });
            }
            catch (TimeoutException)
            {
                // still open a moment later
            }

            if (!Js.Truthy(await wrapper.Run("w => w.querySelector('dialog').open")))
            {
                await Settled(page, i, false);
            }

            return await State();
        }

        return new Heard(wrapper, State, After);
    }

    // The steps both ways of opening share. `open()` opens the dialog again when a step closed it.
    private static async Task Dismissals(IPage page, Heard heard, Func<Task> open, JsonObject facts)
    {
        var (wrapper, _, after) = heard;
        await open();
        await page.Keyboard.PressAsync("Escape");
        facts["Escape"] = await after();

        await open();
        await page.Mouse.ClickAsync(20, 450);
        facts["click outside"] = await after();

        await open();
        var box = (await wrapper.Run("w => { const r = w.querySelector('dialog').getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; }") as JsonArray ?? [])
            .Select(Js.Number).ToArray();
        double[] inside = [box[0] + 6, box[1] + (box[3] / 2)];
        double[] outside = [box[0] > 100 ? 20 : box[0] + box[2] + 40, 450];
        async Task<string> Drag(double[] from, double[] to)
        {
            await page.Mouse.MoveAsync((float)from[0], (float)from[1]);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((float)to[0], (float)to[1]);
            await page.Mouse.UpAsync();
            return await after();
        }

        facts["a press dragged from the panel out"] = await Drag(inside, outside);
        await open();
        facts["a press dragged from outside onto the panel"] = await Drag(outside, inside);

        await open();
        await page.Mouse.ClickAsync((float)inside[0], (float)inside[1]);
        facts["click on the panel's padding"] = await after();
        var buttons = await wrapper.QuerySelectorAllAsync(Close);
        await open();
        await buttons[^1].ClickAsync();
        facts["close button"] = await after();

        if (buttons.Count > 1)
        {
            await open();
            await buttons[0].ClickAsync();
            facts["a modal.close in the content"] = await after();
        }

        await open();
        var before = Js.Number(await page.Run("() => scrollY"));
        await page.Mouse.MoveAsync(20, 450);
        await page.Mouse.WheelAsync(0, 300);
        await page.WaitForTimeoutAsync(200);   // a wheel is answered a frame or two later, and "nothing moved" has no state to wait on
        facts["wheel over the page behind"] = Js.Number(await page.Run("() => scrollY")) == before ? "the page does not scroll" : "the page scrolls";
        await page.Keyboard.PressAsync("Escape");
        await after();
    }

    // What each way of closing it does, with a trigger to open it and to hand focus back to.
    private static async Task<JsonObject> WatchBehaviour(IPage page, int i)
    {
        var heard = await Listen(page, i);
        var wrapper = heard.Wrapper;
        var trigger = (await wrapper.QuerySelectorAsync(Trigger))!;
        async Task Open()
        {
            if (Js.Truthy(await wrapper.Run("w => w.querySelector('dialog').open")))
            {
                return;
            }

            await trigger.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Settled(page, i, true);
            await wrapper.Run("() => { window.__events = []; }");
        }

        var facts = new JsonObject();
        await Open();
        facts["opened from the keyboard"] = await heard.State();
        await page.Keyboard.PressAsync("Tab");
        facts["first Tab"] = await wrapper.Run("w => (w.querySelector('dialog').contains(document.activeElement) ? 'a control in the dialog' : 'outside the dialog')");
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        facts["Shift+Tab past the first control"] = await wrapper.Run("(w, CLOSE) => ([...w.querySelectorAll(CLOSE)].pop() === document.activeElement ? 'the close button, the last control' : 'elsewhere')", Close);
        await Dismissals(page, heard, Open, facts);
        return facts;
    }

    private static async Task<JsonObject> WatchStateBehaviour(IPage page, int i)
    {
        var heard = await Listen(page, i);
        var wrapper = heard.Wrapper;
        // A page that heard `close` says "false"; opening again is its next render saying "true".
        async Task Open()
        {
            if (Js.Truthy(await wrapper.Run("w => w.querySelector('dialog').open")))
            {
                return;
            }

            await Say(page, i, false);
            await Say(page, i, true);
            await wrapper.Run("() => { window.__events = []; }");
        }

        var facts = new JsonObject();
        await Dismissals(page, heard, Open, facts);
        await Open();
        await Say(page, i, false);
        facts["the page says closed"] = await heard.After();
        await Open();
        await page.Keyboard.PressAsync("Escape");
        await heard.After();
        await Open();
        facts["closed by the reader, then the page says open again"] = await heard.State();
        await Say(page, i, false);
        return facts;
    }

    // `String(value)`, and `value ?? '(absent)'`.
    private static string Absent(JsonObject? facts, string key) => facts?[key] is { } value ? JsString.Of(value) : "(absent)";

    private static List<string> CompareFacts(JsonNode? theirsNode, JsonNode? mineNode)
    {
        var (theirs, mine) = (theirsNode as JsonObject ?? [], mineNode as JsonObject);
        return theirs.Select(p => p.Key).Concat(mine?.Select(p => p.Key) ?? []).Distinct(StringComparer.Ordinal)
            .Where(key => !Compare.Same(JsString.Of(theirs, key), JsString.Of(mine, key)))
            .Select(key => $"{key}: {Absent(theirs, key)} vs {Absent(mine, key)}").ToList();
    }

    // Lengths to the 0.6px a box is held to everywhere else.
    private static List<string> CompareCorner(JsonObject theirs, JsonObject? mine)
    {
        static bool Near(string x, string y)
        {
            var (a, b) = (Numbers().Matches(x).Select(m => Js.Parse(m.Value)).ToList(), Numbers().Matches(y).Select(m => Js.Parse(m.Value)).ToList());
            return Numbers().Replace(x, "#") == Numbers().Replace(y, "#") && a.Select((n, i) => i < b.Count && Math.Abs(n - b[i]) <= 0.6).All(ok => ok);
        }

        return theirs.Select(p => p.Key).Where(key => !Near(JsString.Of(theirs, key), JsString.Of(mine, key)))
            .Select(key => $"{key}: {JsString.Of(theirs, key)} vs {Absent(mine, key)}").ToList();
    }

    // The open dialog, compared from the dialog down: where the page happens to put the element that holds it says
    // nothing about a box the viewport places.
    private static List<string> CompareOpen(JsonNode? theirsNode, JsonNode? mineNode)
    {
        static (MeasuredNode? Holder, MeasuredNode? Dialog) Find(Example example, string prefix)
        {
            var holder = example.Nodes.FirstOrDefault(n => n.Has($"{prefix}modal"));
            return (holder, example.Nodes.FirstOrDefault(n => holder is not null && n.Parent == holder.Id && n.Tag == "dialog"));
        }

        var theirs = new Example((JsonObject)(theirsNode?.DeepClone() ?? new JsonObject()));
        var mine = new Example((JsonObject)(mineNode?.DeepClone() ?? new JsonObject()));
        var (a, da) = Find(theirs, "data-flux-");
        var (b, db) = Find(mine, "data-ui-");
        if (da is null || db is null)
        {
            return [$"dialog: {(da is not null ? "only in Flux" : db is not null ? "only in Rask" : "in neither")}"];
        }

        var diffs = new List<string>();
        if (!(ParityComparer.Native.GetValueOrDefault(a!.Tag, a.Tag) == b!.Tag || (a.Tag == "button" && b.Tag == "summary")))
        {
            diffs.Add($"modal: tag <{a.Tag}> vs <{b.Tag}>");
        }

        var departs = Departed.From(mine, b);
        Comparer.CompareStyles(a.Styles, b.Styles, "modal", diffs, departs.Count > 0 ? new HashSet<string>(departs, StringComparer.Ordinal) : null, b.Pressable);
        diffs.AddRange(Comparer.CompareSubtree(theirs, da, mine, db, "dialog"));
        return diffs;
    }

    [GeneratedRegex("; focus on [^;]+")]
    private static partial Regex Focus();

    [GeneratedRegex("^dialog > div\\[1\\] > div\\[1\\] > button\\[2\\]: (text \"\" vs \"[^\"]*\"|children <div span> vs <>)$")]
    private static partial Regex YesSendsNothing();

    [GeneratedRegex("-?\\d+(\\.\\d+)?")]
    private static partial Regex Numbers();
}
