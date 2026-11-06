using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>Holds Ui.Tooltip to Flux's tooltip in the state <c>parity</c> cannot see: SHOWN.</summary>
/// <remarks>
///     <c>parity</c> measures a page as it loads, and a tooltip that has just loaded is closed. This shows each tooltip —
///     hovers its trigger, or clicks it when it is toggleable — on Flux's live page and on the page TooltipShownParity
///     wrote (artifacts/flux-parity/rask/tooltip-shown.html), and compares the box that appears: its size, where it sits
///     against its trigger, every computed style <see cref="Pressables.Styles" /> lists, and its children (the kbd, the
///     paragraphs). Then it walks both through the same pointer and keyboard script and compares what shows and hides it.
///     <para>
///         Cases pair by name. Flux's page shows seven of them; the props it documents without an example (align, gap,
///         offset, interactive, disabled, a trigger that is not a button) are built in Flux's page from its own
///         &lt;ui-tooltip&gt;, around the same trigger the Rask page uses, so its own script places them.
///     </para>
///     <para>
///         What shows a hover tooltip on the Rask page is Rask's RUNTIME (data-rask-tooltip), as in an app: the page is
///         given the runtime's behaviour hooks (<see cref="Runtime" />) before it is walked. Flux's page has Flux's script.
///     </para>
///     <para>
///         On a phone (<c>--view 390x844</c>) there is no hover: each tooltip is shown by a FINGER on its trigger (a tap,
///         for a toggleable one), which is what shows Flux's — it opens on a pointerenter of any pointer, a touch's too —
///         and the walk is the phone's own (<see cref="WalkTap" />). The cases pushed against an edge are left to the
///         landings, which hold them at a phone's edges.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
///     flux parity-tooltip [--all] [--view 390x844]
///     </code>
///     Exit code 1 on any difference. Screenshots land in artifacts/flux-parity/shown/.
/// </remarks>
[FluxCommand("parity-tooltip", "the shown tooltip, its walks and where it lands, against Flux's")]
public sealed partial class ParityTooltip : IFluxCommand
{
    private const string Flux = "https://fluxui.dev/components/tooltip";
    private const string Save = "<button type=\"button\" style=\"width:120px;height:40px;border:1px solid gray\">Save file</button>";
    private const string Small = "<button type=\"button\" style=\"width:40px;height:40px;border:1px solid gray\">o</button>";
    private const string Wide = "A fairly wide tooltip text here";

    // name · where Flux has it (an example on its page, or attributes to build one from) · how it is shown.
    private sealed record Case(string Name, int? Example = null, bool Header = false, bool Click = false, string? Build = null, bool Closed = false,
        string? Trigger = null, string? Rask = null, string? Edge = null, string? Text = null);

    private static readonly Case[] Cases =
    [
        new("intro", Example: 0),
        new("position top", Example: 2),
        new("position right", Example: 3),
        new("position bottom", Example: 4),
        new("position left", Example: 5),
        new("kbd", Header: true),
        new("info", Example: 1, Click: true),
        .. new[] { "top", "right", "bottom", "left" }.SelectMany(side => new[] { "center", "start", "end" }.Select(align =>
            new Case($"{side} {align}", Build: $"position=\"{side} {align}\""))),
        new("gap 12", Build: "position=\"top center\" gap=\"12\""),
        new("offset 20", Build: "position=\"top center\" offset=\"20\""),
        new("offset 20 start", Build: "position=\"top start\" offset=\"20\""),
        new("offset 8 end", Build: "position=\"top end\" offset=\"8\""),
        new("right gap 12 offset 20", Build: "position=\"right center\" gap=\"12\" offset=\"20\""),
        new("left start offset 8", Build: "position=\"left start\" offset=\"8\""),
        new("interactive", Build: "position=\"top center\" interactive"),
        new("disabled", Build: "position=\"top center\" disabled", Closed: true),
        new("span", Build: "position=\"top center\"", Trigger: "<span tabindex=\"0\">plain text</span>"),
        new("disabled button", Build: "position=\"top center\"", Trigger: Save.Replace("<button ", "<button disabled ", StringComparison.Ordinal)),
        // The same four tooltips, pushed against an edge of the viewport: each has to flip, or slide, or both.
        .. new[]
        {
            ("top edge, top", "wide top", "top:4px;left:600px"), ("bottom edge, bottom", "wide bottom", "bottom:4px;left:600px"),
            ("left edge, left", "wide left", "top:300px;left:4px"), ("right edge, right", "wide right", "top:300px;right:4px"),
            ("left edge, top", "wide top", "top:300px;left:4px"), ("right edge, bottom", "wide bottom", "top:300px;right:4px"),
            ("corner, top", "wide top", "top:4px;left:4px"),
        }.Select(edge => new Case(edge.Item1, Rask: edge.Item2, Edge: edge.Item3, Build: $"position=\"{edge.Item2[5..]} center\"", Trigger: Small, Text: Wide)),
    ];

    // How it is placed rather than what it is: Flux's script writes `position:absolute` and page coordinates, Rask.Ui has
    // the browser anchor a fixed box. Where the box ENDS UP is compared, as `at`.
    private static readonly HashSet<string> Mechanism = new(StringComparer.Ordinal) { "position", "width", "height" };

    // The one attribute of Flux's that Rask.Ui does not write, by case and name, with why.
    private static readonly Dictionary<string, string> Unwritten = new(StringComparer.Ordinal)
    {
        ["info/trigger/aria-expanded"] = "Flux's script keeps it on a toggleable trigger. Rask.Ui's is a `popovertarget` button, whose "
            + "expanded state is the browser's own; a written one would never change, and no runtime hook mirrors it.",
    };

    // One step the runtime's hook does differently, printed as OPEN and not failed: it is the hook's to change
    // (src/Rask.Core/Resources/rask-hover.ts), not the component's.
    private const string Dropped = "after script drops focus (blur(), no next element)";

    private static readonly Dictionary<string, string> Open = new(StringComparer.Ordinal)
    {
        [Dropped] = "Flux leaves an INTERACTIVE tooltip open when its trigger loses focus to nothing, until a press outside; "
            + "data-rask-tooltip closes it on any focusout that leaves the wrapper. Tab, Escape and the pointer agree.",
    };

    private const string Content = "[data-flux-tooltip-content], [data-ui-tooltip-content]";

    // Where the pointer rests between steps: the page's left edge, where neither page has a trigger.
    private static readonly float[] Away = [2, 450];

    // Longer than the 100 ms in which Flux looks at one pointer move: a pointer still this long has come to rest.
    private const int Look = 150;

    // Runs in the page. Resolves once nothing about the tooltip has changed for three frames running: whether it shows,
    // its box, its trigger's box and the page's scroll (Flux's docs scroll smoothly, so a trigger asked into view is
    // still travelling when the call returns). Three seconds at most; a case that never rests is measured as it is.
    private const string SettleScript = """
        (el, selector) => new Promise(resolve => {
          const content = el.querySelector(selector);
          const read = () => {
            const style = getComputedStyle(content);
            const boxes = [content, el.firstElementChild].flatMap(node => { const r = node.getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; });
            return [style.display, style.visibility, content.matches(':popover-open'), scrollX, scrollY, ...boxes].join();
          };
          let last = read();
          let same = 0;
          let frames = 0;
          const tick = () => {
            const now = read();
            same = now === last ? same + 1 : 0;
            last = now;
            if (same >= 3 || ++frames > 180) resolve(same >= 3);
            else requestAnimationFrame(tick);
          };
          requestAnimationFrame(tick);
        })
        """;

    // Runs in the page. The shown content against its trigger.
    private const string MeasureScript = """
        (tooltip, { STYLES }) => {
          const trigger = tooltip.firstElementChild;
          const content = tooltip.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
          const computed = getComputedStyle(content);
          const fix = v => Math.round(v * 100) / 100;
          const rect = el => el.getBoundingClientRect();
          const t = rect(trigger);
          const c = rect(content);
          const style = el => Object.fromEntries(STYLES.map(key => [key, getComputedStyle(el)[key]]));
          const shown = computed.display !== 'none' && computed.visibility !== 'hidden' && c.width > 0;
          const x = Math.max(0, Math.min(t.x, c.x) - 12);
          const y = Math.max(0, Math.min(t.y, c.y) - 12);
          // What a screen reader is told: the ARIA and role each side writes, an id reduced to whether it is the content's.
          const said = el => Object.fromEntries([...el.attributes]
            .filter(a => a.name.startsWith('aria-') || a.name === 'role')
            .map(a => [a.name, a.value === content.id ? '#content' : a.value]));
          return {
            shown,
            said: { trigger: said(trigger), content: said(content) },
            topLayer: content.matches(':popover-open'),
            // left and top against the trigger's, then the size.
            at: [fix(c.x - t.x), fix(c.y - t.y), fix(c.width), fix(c.height)],
            inViewport: [fix(c.x), fix(c.y)],
            // Without whitespace: what sits between two paragraphs is Blade's indentation, and draws nothing.
            text: content.textContent.replace(/\s+/g, ''),
            style: style(content),
            children: [...content.children].map(child => {
              const k = rect(child);
              return { tag: child.tagName.toLowerCase(), at: [fix(k.x - c.x), fix(k.y - c.y), fix(k.width), fix(k.height)], style: style(child) };
            }),
            clip: { x, y, width: Math.max(t.right, c.right) + 12 - x, height: Math.max(t.bottom, c.bottom) + 12 - y },
          };
        }
        """;

    private const string Showing = """
        el => {
          const content = el.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
          const computed = getComputedStyle(content);
          return computed.display !== 'none' && computed.visibility !== 'hidden';
        }
        """;

    private const string ScrollIntoView = "el => el.scrollIntoView({ block: 'center', behavior: 'instant' })";

    private IBrowser browser = null!;
    private View view = null!;
    private readonly Dictionary<IPage, ICDPSession> fingers = [];

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var raskPage = Repo.Parity("rask", "tooltip-shown.html");
        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        view = View.Of(arg);
        browser = await Browsers.Chromium();
        var failures = 0;
        foreach (var scheme in new[] { "light", "dark" })
        {
            var (flux, rask) = await Pages(scheme, raskPage);
            // The kbd case is the docs header's own button, which Flux's site does not draw on a phone.
            foreach (var test in Cases.Where(test => !(view.Phone && (test.Edge is not null || test.Header))))
            {
                var theirs = await Show(flux, test, "flux", scheme);
                var mine = await Show(rask, test, "rask", scheme);
                var diffs = CompareShown(theirs, mine, test);
                failures += diffs.Count > 0 ? 1 : 0;
                var at = Numbers(theirs["at"]);
                var where = Js.Truthy(theirs["shown"]) ? $"{Js.Num(at[2])}x{Js.Num(at[3])} at {Js.Num(at[0])},{Js.Num(at[1])}" : "closed";
                var top = Js.Truthy(mine["shown"]) ? (Js.Truthy(mine["topLayer"]) ? " · top layer" : " · in place") : "";
                Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {scheme} {test.Name,-24} {where}{top}");
                foreach (var d in diffs.Take(arg.Flag("all") ? int.MaxValue : 10))
                {
                    Console.WriteLine($"       {d}");
                }
            }

            // A phone's walk starts on fresh pages, where no finger has shown a tooltip yet.
            if (scheme == "light" && !view.Phone)
            {
                failures += await Behaviour(flux, rask);
            }

            await flux.Context.CloseAsync();
            await rask.Context.CloseAsync();
            if (scheme == "light" && view.Phone)
            {
                var (f, r) = await Pages(scheme, raskPage);
                failures += await PhoneBehaviour(f, r);
            }
        }

        // Where it lands with no room: the tooltips of Flux's own page — over, beside, under and before their trigger —
        // shown with the trigger held at the edges of a phone and of a desktop (Placement). The desktop's run holds both,
        // so a phone's leaves it out.
        var astray = view.Phone ? 0 : await Placement.Landings(browser, new Placement.Family("tooltip",
            new Placement.Side(Flux, "[data-preview-wrapper] ui-tooltip[data-flux-tooltip]", "[data-flux-tooltip-content]", null),
            new Placement.Side(Repo.FileUrl(Repo.Parity("rask", "tooltip.html")), "[data-preview-wrapper] [data-ui-tooltip]:not([data-toggleable])", "[data-ui-tooltip-content]", null),
            [.. Enumerable.Range(0, 5).Select(index => new Placement.Host(index, "hover"))])
        {
            Refresh = arg.Flag("refresh"),
            All = arg.Flag("all"),
        });
        await browser.CloseAsync();
        Console.WriteLine(failures > 0 ? $"\nflux parity: the shown tooltip differs in {failures} case(s)." : "\nflux parity: the shown tooltip matches Flux.");
        Console.WriteLine(astray > 0 ? $"flux parity: the tooltip lands elsewhere for {astray} tooltip(s)." : "flux parity: the tooltip lands where Flux's does.");
        return failures > 0 || astray > 0 ? 1 : 0;
    }

    private static double[] Numbers(JsonNode? node) => (node as JsonArray ?? []).Select(Js.Number).ToArray();

    // Flux's page with the cases built into it, and the Rask page with the runtime's hooks.
    private async Task<(IPage Flux, IPage Rask)> Pages(string scheme, string raskPage)
    {
        var flux = await OpenPage(scheme, Flux);
        await PrepareFlux(flux);
        var rask = await OpenPage(scheme, Repo.FileUrl(raskPage));
        await Runtime.With(rask, "rask-hooks.ts");
        return (flux, rask);
    }

    private async Task<IPage> OpenPage(string scheme, string url)
    {
        var options = view.Context();
        options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
        var context = await browser.NewContextAsync(options);
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        // Every declared face, not only those the closed page uses: the medium a tooltip is set in is first needed when
        // the first one shows, and would be measured in the fallback face while it loads.
        await page.Run("""
            async () => {
              await Promise.all([...document.fonts].map(face => face.load().catch(() => {})));
              await document.fonts.ready;
            }
            """);
        return page;
    }

    // Builds, inside Flux's own page, the cases its page does not show. The content's classes are taken from a live
    // tooltip at run time, so the box is Flux's and only the props under test are ours.
    private static async Task PrepareFlux(IPage page)
    {
        var cases = new JsonArray([.. Cases.Where(test => test.Build is not null).Select(test =>
        {
            var json = new JsonObject { ["name"] = test.Name, ["build"] = test.Build };
            if (test.Trigger is not null)
            {
                json["trigger"] = test.Trigger;
            }

            if (test.Text is not null)
            {
                json["text"] = test.Text;
            }

            return (JsonNode)json;
        })]);
        await page.Run("""
            ({ cases, SAVE }) => {
              const live = document.querySelector('[data-preview-wrapper] ui-tooltip');
              const look = live.querySelector('[data-flux-tooltip-content]').className;
              const stage = document.createElement('div');
              stage.style.cssText = 'display:flex;flex-wrap:wrap;gap:100px 150px;justify-content:center;padding:60px 120px';
              live.closest('[data-preview-wrapper]').appendChild(stage);
              for (const test of cases) {
                const host = document.createElement('div');
                host.dataset.case = test.name;
                host.innerHTML = `<ui-tooltip data-flux-tooltip ${test.build}>${test.trigger ?? SAVE}`
                  + `<div popover="manual" data-flux-tooltip-content role="tooltip" class="${look}">${test.text ?? 'Settings'}</div></ui-tooltip>`;
                stage.appendChild(host);
              }
            }
            """, new JsonObject { ["cases"] = cases, ["SAVE"] = Save });
        // Wired, not waited for: Flux's element gives each content its id when it has taken the case over.
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('[data-case] [data-flux-tooltip-content]')].every(content => content.id)", null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static ILocator Locate(IPage page, Case test, string side)
    {
        if (side == "rask")
        {
            return page.Locator($"[data-case=\"{test.Rask ?? test.Name}\"] [data-ui-tooltip]");
        }

        if (test.Header)
        {
            return page.Locator("[data-flux-tooltip]:has(> [tooltip\\:kbd])").First;
        }

        if (test.Build is not null)
        {
            return page.Locator($"[data-case=\"{test.Name}\"] [data-flux-tooltip]");
        }

        return page.Locator("[data-preview-wrapper] [data-flux-tooltip]:not([data-case] *)").Nth(test.Example ?? 0);
    }

    private static ILocator Trigger(ILocator tooltip) => tooltip.Locator(":scope > :first-child");

    private static async Task Swallow(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (TimeoutException)
        {
            // as the script had it: on as before
        }
        catch (PlaywrightException)
        {
            // as the script had it: on as before
        }
    }

    private async Task<JsonObject> Show(IPage page, Case test, string side, string scheme)
    {
        var tooltip = Locate(page, test, side);
        var trigger = Trigger(tooltip);
        var host = page.Locator($"[data-case=\"{(side == "rask" ? test.Rask ?? test.Name : test.Name)}\"]");
        // Mid-viewport, so nothing flips that was not pushed against an edge on purpose.
        if (test.Edge is not null)
        {
            await host.Run("(el, css) => { el.style.cssText = `position:fixed;z-index:99999;${css}`; }", test.Edge);
        }

        // On a phone, 390px leave a 320px tooltip no room to centre on a trigger off the middle, so each is held in the
        // middle of the screen on both pages: the two pages lay their examples out differently.
        else if (view.Phone)
        {
            await Mid(tooltip, true);
        }
        else
        {
            await trigger.Run(ScrollIntoView);
        }

        // On a phone too: once a finger has shown one tooltip Flux shows no other to a finger, until a mouse moves.
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await Settle(tooltip);
        // A finger that rests on the trigger, measured before it lifts: an instant tap leaves Flux's tooltip shown only
        // when its mouse events land in the frame it showed in, which a loaded machine does not promise.
        if (view.Phone && test.Click)
        {
            await Tap(page, trigger);
        }
        else if (view.Phone)
        {
            await Finger(page, trigger, true);
        }
        else if (test.Click)
        {
            await trigger.ClickAsync();
        }
        else
        {
            await trigger.HoverAsync(new LocatorHoverOptions { Force = true });
        }

        // On the state, not on a delay: showing, then the same box for three frames (Flux places its popover a frame
        // after it opens it). A case that must stay closed only has to hold still.
        if (!test.Closed)
        {
            await Swallow(() => tooltip.Locator(Content).And(page.Locator(":popover-open")).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 3000 }));
        }

        await Settle(tooltip);
        var measured = (JsonObject)(await tooltip.Run(MeasureScript, new { STYLES = Pressables.Styles.Where(key => !Mechanism.Contains(key)).ToArray() }))!;
        if (Js.Truthy(measured["shown"]))
        {
            var dir = Repo.Parity($"shown{view.Sized}", side, scheme);
            Directory.CreateDirectory(dir);
            var clip = measured["clip"]!;
            await Swallow(() => page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(dir, $"{Separators().Replace(test.Name, "-")}.png"),
                Clip = new Clip { X = (float)Js.Number(clip["x"]), Y = (float)Js.Number(clip["y"]), Width = (float)Js.Number(clip["width"]), Height = (float)Js.Number(clip["height"]) },
            }));
        }

        if (test.Click)
        {
            await (view.Phone ? Tap(page, trigger) : trigger.ClickAsync());
        }
        else if (view.Phone)
        {
            await Finger(page, null, false);
        }

        if (test.Edge is not null)
        {
            await host.Run("el => { el.style.cssText = ''; }");
        }

        if (view.Phone)
        {
            await Mid(tooltip, false);
            await Screen.Press(page, Away[0], Away[1], true);
        }
        else
        {
            await page.Mouse.MoveAsync(Away[0], Away[1]);
        }

        // Closed again before the next case starts: an open neighbour would be in its screenshot, and a page still moving
        // back from an edge would be hovered where the trigger no longer is.
        await Swallow(() => tooltip.Locator(Content).And(page.Locator(":popover-open")).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 3000 }));
        await Settle(tooltip);
        return measured;
    }

    // Holds a tooltip's root in the middle of the viewport, or puts it back where it was.
    private static Task Mid(ILocator tooltip, bool held) => tooltip.Run("""
        (el, held) => {
          if (!held) { el.style.cssText = el.dataset.parityStyle ?? ''; return; }
          el.dataset.parityStyle = el.style.cssText;
          // The trigger centred: a root may be wider than it.
          Object.assign(el.style, { position: 'fixed', zIndex: '99999', margin: '0', width: 'max-content', left: '0', top: `${Math.round(innerHeight / 2)}px` });
          const trigger = el.firstElementChild.getBoundingClientRect();
          el.style.left = `${Math.round((innerWidth - trigger.width) / 2 - trigger.x)}px`;
        }
        """, held);

    private static Task Settle(ILocator tooltip) => tooltip.Run(SettleScript, Content);

    private static List<string> CompareShown(JsonObject theirs, JsonObject mine, Case test)
    {
        var diffs = new List<string>();
        var (fluxShown, raskShown) = (Js.Truthy(theirs["shown"]), Js.Truthy(mine["shown"]));
        if (fluxShown == test.Closed)
        {
            diffs.Add($"Flux {(fluxShown ? "shows" : "does not show")} it — the case is wrong");
        }

        foreach (var part in new[] { "trigger", "content" })
        {
            var (x, y) = (theirs["said"]![part] as JsonObject ?? [], mine["said"]![part] as JsonObject ?? []);
            foreach (var name in x.Select(p => p.Key).Concat(y.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
            {
                if (JsString.Json(x, name) == JsString.Json(y, name) || Unwritten.ContainsKey($"{test.Name}/{part}/{name}"))
                {
                    continue;
                }

                diffs.Add($"{part} {name}: {JsString.Of(x, name)} vs {JsString.Of(y, name)}");
            }
        }

        if (fluxShown != raskShown)
        {
            diffs.Add($"shown: {(fluxShown ? "true" : "false")} vs {(raskShown ? "true" : "false")}");
            return diffs;
        }

        if (!fluxShown)
        {
            return diffs;
        }

        if (JsString.Of(theirs, "text") != JsString.Of(mine, "text"))
        {
            diffs.Add($"text \"{JsString.Of(theirs, "text")}\" vs \"{JsString.Of(mine, "text")}\"");
        }

        Box(theirs["at"], mine["at"], "against its trigger", diffs);
        if (test.Edge is not null)
        {
            Box(theirs["inViewport"], mine["inViewport"], "in the viewport", diffs);
        }

        Styles((JsonObject)theirs["style"]!, (JsonObject)mine["style"]!, "content", diffs);
        var (ca, cb) = ((theirs["children"] as JsonArray ?? []).OfType<JsonObject>().ToList(), (mine["children"] as JsonArray ?? []).OfType<JsonObject>().ToList());
        if (ca.Count != cb.Count)
        {
            diffs.Add($"children <{string.Join(' ', ca.Select(c => JsString.Of(c, "tag")))}> vs <{string.Join(' ', cb.Select(c => JsString.Of(c, "tag")))}>");
            return diffs;
        }

        for (var i = 0; i < ca.Count; i++)
        {
            var tag = JsString.Of(ca[i], "tag");
            if (tag != JsString.Of(cb[i], "tag"))
            {
                diffs.Add($"child {i}: <{tag}> vs <{JsString.Of(cb[i], "tag")}>");
            }

            Box(ca[i]["at"], cb[i]["at"], $"{tag}[{i}] inside it", diffs);
            Styles((JsonObject)ca[i]["style"]!, (JsonObject)cb[i]["style"]!, $"{tag}[{i}]", diffs);
        }

        return diffs;
    }

    // The tolerances parity holds a loaded page to.
    private static void Box(JsonNode? a, JsonNode? b, string what, List<string> diffs)
    {
        var (x, y) = (Numbers(a), Numbers(b));
        if (x.Select((v, i) => Math.Abs(v - (i < y.Length ? y[i] : double.NaN)) > 0.6).Any(off => off))
        {
            diffs.Add($"{what}: {JsString.Of(a)} vs {JsString.Of(b)}");
        }
    }

    private static void Styles(JsonObject x, JsonObject y, string where, List<string> diffs)
    {
        foreach (var (key, _) in x)
        {
            if (Same(Js.Str(x[key]), Js.Str(y[key])))
            {
                continue;
            }

            var side = BorderColor().Match(key);
            if (side.Success && Js.Str(x[$"border{side.Groups[1].Value}Width"]) == "0px" && Js.Str(y[$"border{side.Groups[1].Value}Width"]) == "0px")
            {
                continue;
            }

            diffs.Add($"{where}: {key}: {JsString.Of(x, key)} vs {JsString.Of(y, key)}");
        }
    }

    // Decimals rounded to thousandths, and nothing else let go.
    private static bool Same(string? x, string? y)
    {
        if (x == y)
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        string Round(string v) => Decimal().Replace(v, m => Js.Num(Js.Round(Js.Parse(m.Value) * 1000) / 1000));
        return Round(x) == Round(y);
    }

    // ----- What shows it and what hides it ----------------------------------------------------------------------------
    // Walked the same way on both pages. On the Rask page one hook shows every hover tooltip, whatever its trigger; a
    // button and a plain <span> are both walked, because the button was once the browser's own invoker and the span the
    // stylesheet's :hover, and the two disagreed.
    private async Task<int> Behaviour(IPage flux, IPage rask)
    {
        Case Named(string name) => Cases.First(test => test.Name == name);
        var walked = new OrderedDictionary<string, OrderedDictionary<string, object?>>(StringComparer.Ordinal)
        {
            ["flux"] = await WalkHover(flux, Locate(flux, Named("top center"), "flux")),
            ["rask:button"] = await WalkHover(rask, Locate(rask, Named("top center"), "rask")),
            ["rask:span"] = await WalkHover(rask, Locate(rask, Named("span"), "rask")),
        };
        var expanded = Both(await WalkExpanded(flux, Locate(flux, Named("interactive"), "flux")), await WalkExpanded(rask, Locate(rask, Named("interactive"), "rask")));
        var toggled = Both(await WalkToggle(flux, Locate(flux, Named("info"), "flux")), await WalkToggle(rask, Locate(rask, Named("info"), "rask")));
        return Report([("hover", walked), ("interactive: aria-expanded on the trigger", expanded), ("toggleable", toggled)]);
    }

    private static OrderedDictionary<string, OrderedDictionary<string, object?>> Both(OrderedDictionary<string, object?> flux, OrderedDictionary<string, object?> rask) =>
        new(StringComparer.Ordinal) { ["flux"] = flux, ["rask"] = rask };

    // The same on a phone, where every step is a finger's. Each walk takes tooltips no finger has shown yet.
    private async Task<int> PhoneBehaviour(IPage flux, IPage rask)
    {
        ILocator Named(IPage page, string side, string name) => Locate(page, Cases.First(test => test.Name == name), side);
        // Flux's own examples, so that what is walked is all Flux's.
        var tapped = Both(await WalkTap(flux, Named(flux, "flux", "intro"), Named(flux, "flux", "position top")),
            await WalkTap(rask, Named(rask, "rask", "intro"), Named(rask, "rask", "position top")));
        var expanded = Both(await WalkTapExpanded(flux, Named(flux, "flux", "interactive")), await WalkTapExpanded(rask, Named(rask, "rask", "interactive")));
        var toggled = Both(await WalkTapToggle(flux, Named(flux, "flux", "info")), await WalkTapToggle(rask, Named(rask, "rask", "info")));
        var result = Report([("tap", tapped), ("interactive: aria-expanded on the trigger", expanded), ("toggleable", toggled)]);
        await flux.Context.CloseAsync();
        await rask.Context.CloseAsync();
        return result;
    }

    // String(value), as a step's value is compared; and as Array.join writes it (null is empty).
    private static string Said(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "",
    };

    // Prints each walk step by step, Flux's first; answers how many steps differ from Flux's.
    private static int Report(IEnumerable<(string Title, OrderedDictionary<string, OrderedDictionary<string, object?>> Runs)> tables)
    {
        var failed = 0;
        Console.WriteLine("\nbehaviour (true = the tooltip is showing after the step)");
        foreach (var (title, runs) in tables)
        {
            var labels = runs.Keys.ToList();
            Console.WriteLine($"  {title}: {string.Join(" | ", labels)}");
            foreach (var (step, theirs) in runs["flux"])
            {
                var values = labels.Select(label => runs[label].GetValueOrDefault(step)).ToList();
                // A delay is a number of milliseconds and is printed, not compared: both show within a frame.
                var off = labels.Where(label => Said(runs[label].TryGetValue(step, out var v) ? v : "undefined") != Said(theirs) && !Delay().IsMatch(Said(theirs))).ToList();
                var open = off.Count > 0 ? Open.GetValueOrDefault(step) : null;
                failed += off.Count > 0 && open is null ? 1 : 0;
                Console.WriteLine($"  {(off.Count > 0 ? (open is not null ? "OPEN " : "FAIL ") : "ok   ")} {step,-46} {string.Join(" | ", values.Select(v => v is null ? "" : Said(v)))}");
                if (open is not null)
                {
                    Console.WriteLine($"           {open}");
                }
            }
        }

        return failed;
    }

    // The state Flux writes on an interactive tooltip's trigger, at rest and through one showing.
    private async Task<OrderedDictionary<string, object?>> WalkExpanded(IPage page, ILocator tooltip)
    {
        var trigger = Trigger(tooltip);
        Task<string?> State() => trigger.GetAttributeAsync("aria-expanded");
        var steps = new OrderedDictionary<string, object?>(StringComparer.Ordinal);

        await trigger.Run(ScrollIntoView);
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await Settle(tooltip);
        steps["at rest"] = await State();
        await trigger.HoverAsync(new LocatorHoverOptions { Force = true });
        await Settle(tooltip);
        steps["while the pointer shows it"] = await State();
        await page.Keyboard.PressAsync("Escape");
        await Settle(tooltip);
        steps["after Escape"] = await State();
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await Settle(tooltip);
        await page.Keyboard.PressAsync("Shift");
        await trigger.FocusAsync();
        await Settle(tooltip);
        steps["while keyboard focus shows it"] = await State();
        await page.Keyboard.PressAsync("Tab");
        await Settle(tooltip);
        steps["after Tab moves focus on"] = await State();
        await page.Keyboard.PressAsync("Shift");
        await trigger.FocusAsync();
        await Settle(tooltip);
        await trigger.Run("el => el.blur()");
        await Settle(tooltip);
        steps[Dropped] = await State();
        await page.Mouse.ClickAsync(20, 20);
        await Settle(tooltip);
        return steps;
    }

    private static async Task<bool> IsShowing(ILocator tooltip) => Js.Truthy(await tooltip.Run(Showing));

    private async Task<OrderedDictionary<string, object?>> WalkHover(IPage page, ILocator tooltip)
    {
        var trigger = Trigger(tooltip);
        // Milliseconds from the pointer arriving (or leaving) to the frame the tooltip has changed in.
        async Task<string> Timed(Func<Task> act, bool want)
        {
            await tooltip.Run("""
                (el, want) => {
                  const content = el.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
                  const visible = () => getComputedStyle(content).display !== 'none' && getComputedStyle(content).visibility !== 'hidden';
                  window.__timed = null;
                  let from = 0;
                  const frame = () => {
                    if (visible() === want) window.__timed = Math.round(performance.now() - from);
                    else requestAnimationFrame(frame);
                  };
                  const arm = () => { from = performance.now(); frame(); };
                  el.firstElementChild.addEventListener(want ? 'pointerenter' : 'pointerleave', arm, { once: true });
                }
                """, want);
            await act();
            await Swallow(() => page.WaitForFunctionAsync("() => window.__timed !== null", null, new PageWaitForFunctionOptions { Timeout = 3000 }));
            return Js.Str(await page.Run("() => (window.__timed === null ? 'never' : `${window.__timed}ms`)")) ?? "";
        }

        // From rest: Flux looks at a pointer move at most once in Look ms (rask-hover.ts says the same), so a move made
        // sooner after the last one looked at is passed over, and where the walk then stands is a race against that clock.
        async Task Rest()
        {
            await page.WaitForTimeoutAsync(Look);
            await page.Mouse.MoveAsync(Away[0], Away[1]);
            await Settle(tooltip);
        }

        Task Hover() => trigger.HoverAsync(new LocatorHoverOptions { Force = true });
        var steps = new OrderedDictionary<string, object?>(StringComparer.Ordinal);

        await trigger.Run(ScrollIntoView);
        await Rest();
        steps["delay before it shows"] = await Timed(Hover, true);
        steps["hover shows it"] = await IsShowing(tooltip);
        steps["delay before it hides"] = await Timed(() => page.Mouse.MoveAsync(Away[0], Away[1]), false);
        steps["pointer leaving hides it"] = !await IsShowing(tooltip);

        // Scrolled by script, whichever way the page can go: the pointer does not move, so nothing leaves.
        await Hover();
        await Settle(tooltip);
        async Task<double[]> Gap() => Numbers(await tooltip.Run("""
            el => {
              const content = el.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
              return [el.firstElementChild.getBoundingClientRect().y, content.getBoundingClientRect().y].map(Math.round);
            }
            """));
        var before = await Gap();
        // At once: the docs page scrolls smoothly, and a scroll still under way is a trigger still moving.
        await page.Run("() => { const y = scrollY; scrollBy({ top: 8, behavior: 'instant' }); if (scrollY === y) scrollBy({ top: -8, behavior: 'instant' }); }");
        await Settle(tooltip);
        var after = await Gap();
        steps["follows its trigger on scroll"] = after[0] != before[0] && after[0] - after[1] == before[0] - before[1];
        await page.Run("() => scrollBy(0, 0)");
        await Rest();

        await Hover();
        await Settle(tooltip);
        var box = (await trigger.BoundingBoxAsync())!;
        var content = (await tooltip.Locator(Content).BoundingBoxAsync())!;
        // From rest at the trigger's edge, ONE unbroken move onto the tooltip, as a hand makes it: its steps come well
        // inside one look, so neither page sees it over the gap. Then it rests there, longer than a look.
        await page.WaitForTimeoutAsync(Look);
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + 1);
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), content.Y + (content.Height / 2), new MouseMoveOptions { Steps = 6 });
        await page.WaitForTimeoutAsync(Look);
        await Settle(tooltip);
        steps["pointer resting on the tooltip keeps it"] = await IsShowing(tooltip);
        await Rest();

        await Hover();
        await Settle(tooltip);
        await page.Keyboard.PressAsync("Escape");
        await Settle(tooltip);
        steps["Escape hides it"] = !await IsShowing(tooltip);
        await Rest();
        await Hover();
        await Settle(tooltip);
        steps["hovering again after Escape shows it"] = await IsShowing(tooltip);

        await page.Mouse.DownAsync();
        await Settle(tooltip);
        steps["pressing the trigger hides it"] = !await IsShowing(tooltip);
        await page.Mouse.UpAsync();
        await Settle(tooltip);
        steps["shown again after the press is released"] = await IsShowing(tooltip);
        await Rest();
        await page.Run("() => { document.activeElement?.blur(); }");

        // A key first, so the focus that follows is the keyboard's (:focus-visible).
        await page.Keyboard.PressAsync("Shift");
        await trigger.FocusAsync();
        await Settle(tooltip);
        steps["keyboard focus shows it"] = await IsShowing(tooltip);
        await Hover();
        await Rest();
        steps["stays while focused, pointer gone"] = await IsShowing(tooltip);
        await page.Keyboard.PressAsync("Escape");
        await Settle(tooltip);
        steps["Escape hides it while focused"] = !await IsShowing(tooltip);
        await trigger.FocusAsync();
        await trigger.Run("el => el.blur()");
        await Settle(tooltip);
        steps["blur hides it"] = !await IsShowing(tooltip);

        await Rest();
        return steps;
    }

    private static async Task<bool> Displayed(ILocator content) => Js.Truthy(await content.Run("el => getComputedStyle(el).display !== 'none'"));

    private async Task<OrderedDictionary<string, object?>> WalkToggle(IPage page, ILocator tooltip)
    {
        var trigger = Trigger(tooltip);
        var content = tooltip.Locator(Content);
        var steps = new OrderedDictionary<string, object?>(StringComparer.Ordinal);

        await trigger.Run(ScrollIntoView);
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await trigger.HoverAsync();
        await Settle(tooltip);
        steps["hover shows it"] = await Displayed(content);
        await trigger.ClickAsync();
        await Settle(tooltip);
        steps["a click shows it"] = await Displayed(content);
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await Settle(tooltip);
        steps["stays when the pointer leaves"] = await Displayed(content);
        var box = (await content.BoundingBoxAsync())!;
        await page.Mouse.ClickAsync(box.X + 20, box.Y + 20);
        await Settle(tooltip);
        steps["stays on a click inside it"] = await Displayed(content);
        await trigger.ClickAsync();
        await Settle(tooltip);
        steps["a second click hides it"] = !await Displayed(content);
        await trigger.ClickAsync();
        await Settle(tooltip);
        await page.Keyboard.PressAsync("Escape");
        await Settle(tooltip);
        steps["Escape hides it"] = !await Displayed(content);
        await trigger.ClickAsync();
        await Settle(tooltip);
        await page.Mouse.ClickAsync(20, 20);
        await Settle(tooltip);
        steps["a click outside hides it"] = !await Displayed(content);
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Settle(tooltip);
        steps["Enter on the trigger shows it"] = await Displayed(content);
        await page.Keyboard.PressAsync("Escape");
        await Settle(tooltip);
        return steps;
    }

    // A tap in the middle of `locator`, after it is brought into view. Over once its click has come: the browser makes
    // the mouse events of a tap a little after the touch, and a press that hides a tooltip is among them.
    private static async Task Tap(IPage page, ILocator locator)
    {
        await locator.Run(ScrollIntoView);
        var box = (await locator.BoundingBoxAsync())!;
        await page.Run("() => { window.__clicked = new Promise(done => addEventListener('click', done, { once: true, capture: true })); }");
        await Screen.Press(page, box.X + (box.Width / 2), box.Y + (box.Height / 2), true);
        await page.Run("() => Promise.race([window.__clicked, new Promise(done => setTimeout(done, 1000))]).then(() => undefined)");
    }

    // A finger put down on `locator`'s middle (brought into view first), or lifted again.
    private async Task Finger(IPage page, ILocator? locator, bool down)
    {
        if (!fingers.TryGetValue(page, out var cdp))
        {
            cdp = await page.Context.NewCDPSessionAsync(page);
            fingers[page] = cdp;
        }

        if (!down)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
            await page.WaitForTimeoutAsync(300);
            return;
        }

        ArgumentNullException.ThrowIfNull(locator);
        await locator.Run(ScrollIntoView);
        var box = (await locator.BoundingBoxAsync())!;
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = box.X + (box.Width / 2), ["y"] = box.Y + (box.Height / 2) } },
        });
        await page.WaitForTimeoutAsync(100);
    }

    // What a finger does to a hover tooltip, measured on Flux's page (2026-10-27). The finger's pointerenter shows it, as
    // a mouse's does on a desktop; lifting the finger is a press, which hides a tooltip that was showing before the frame
    // it lands in. So a real finger, which rests longer than a frame, shows it only while it is down — and an instant tap
    // (Playwright's) leaves it shown, as long as the tap's mouse events come in the frame it showed in: a race this walk
    // does not run. Then a second tap or a tap elsewhere hides it. And once a finger has shown one, no finger shows any
    // tooltip of the page again, not even another, until a MOUSE moves.
    private async Task<OrderedDictionary<string, object?>> WalkTap(IPage page, ILocator first, ILocator second)
    {
        var steps = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        async Task Rests(ILocator tooltip, string step)
        {
            await Finger(page, Trigger(tooltip), true);
            await Settle(tooltip);
            steps[step] = await IsShowing(tooltip);
            await Finger(page, null, false);
            await Settle(tooltip);
            steps[$"{step}, lifted"] = await IsShowing(tooltip);
        }

        await Rests(first, "a finger resting on it");
        await Rests(first, "a finger on it again");
        await Rests(second, "a finger on another");
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await Rests(second, "a mouse moves, then a finger on that one");
        return steps;
    }

    // The state Flux writes on an interactive tooltip's trigger, through one finger.
    private async Task<OrderedDictionary<string, object?>> WalkTapExpanded(IPage page, ILocator tooltip)
    {
        var trigger = Trigger(tooltip);
        Task<string?> State() => trigger.GetAttributeAsync("aria-expanded");
        var steps = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        await page.Mouse.MoveAsync(Away[0], Away[1]);
        await Settle(tooltip);
        steps["at rest"] = await State();
        await Finger(page, trigger, true);
        await Settle(tooltip);
        steps["while a finger rests on it"] = await State();
        await Finger(page, null, false);
        await Settle(tooltip);
        steps["once the finger has lifted"] = await State();
        return steps;
    }

    private static async Task<OrderedDictionary<string, object?>> WalkTapToggle(IPage page, ILocator tooltip)
    {
        var trigger = Trigger(tooltip);
        var content = tooltip.Locator(Content);
        async Task<bool> Tapped()
        {
            await Tap(page, trigger);
            await Settle(tooltip);
            return await IsShowing(tooltip);
        }

        var steps = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        steps["a tap shows it"] = await Tapped();
        await page.WaitForTimeoutAsync(1000);
        steps["it stays once the finger has lifted"] = await IsShowing(tooltip);
        var box = (await content.BoundingBoxAsync())!;
        await Screen.Press(page, box.X + 20, box.Y + 20, true);
        await Settle(tooltip);
        steps["stays on a tap inside it"] = await IsShowing(tooltip);
        steps["a second tap hides it"] = !await Tapped();
        steps["a third tap shows it again"] = await Tapped();
        await Screen.Press(page, Away[0], Away[1], true);
        await Settle(tooltip);
        steps["a tap elsewhere hides it"] = !await IsShowing(tooltip);
        return steps;
    }

    [GeneratedRegex("[ ,]+")]
    private static partial Regex Separators();

    [GeneratedRegex("ms$|never$")]
    private static partial Regex Delay();

    [GeneratedRegex("^border(Top|Right|Bottom|Left)Color$")]
    private static partial Regex BorderColor();

    [GeneratedRegex("-?[0-9]*\\.[0-9]+(e-?[0-9]+)?")]
    private static partial Regex Decimal();
}
