using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds what FOCUS draws on Rask.Ui's controls to what it draws on Flux UI's: after a mouse press, and after the
///     keyboard.
/// </summary>
/// <remarks>
///     <c>parity</c> forces <c>:focus</c> and <c>:focus-visible</c> together on each control, which cannot tell a
///     <c>focus:</c> ring from a <c>focus-visible:</c> one, and never moves focus for real — so it cannot see a ring a
///     CLICK leaves, or one drawn on what focus lands on next (a wrapper, an opened popup). This walks each control for
///     real, on Flux's live page and on the page FluxParityPages wrote, and compares where focus is (the control itself,
///     its popup, the page) and whether the browser calls it <c>:focus-visible</c>; and every ring the move drew anywhere
///     on the page — an element whose outline is shown, or whose box-shadow or border colour is not what it was before
///     the move — by what is drawn: outline style, width, colour, offset, the box-shadow, the border colour.
///     <para>
///         Each control is moved three ways: <c>mouse</c> (a real press at its centre), <c>keyboard</c> (focus arrives
///         with the keyboard as the last thing used: a Tab, then the control) and, for a control that opens a popup,
///         <c>open</c> (focused that way, then Enter). In Chromium, and in WebKit as well where it is installed
///         (<c>--webkit</c>), since Safari draws its own blue ring.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux parity-focus [--webkit] [--only &lt;name&gt;] [--shots &lt;dir&gt;] [--all]
///     </code>
///     Exit code 1 on any difference. <c>--shots &lt;dir&gt;</c> writes a 1280-wide screenshot of every step on both sides.
/// </remarks>
[FluxCommand("parity-focus", "what focus draws after a press and after the keyboard, against Flux's")]
public sealed partial class ParityFocus : IFluxCommand
{
    // name · Flux's page and the control on it · the Rask page and its control · whether it opens a popup. Each control
    // is the first of its kind in its example, so both pages hold the same one. A third selector is what the pointer
    // presses, where that is not the control: the kit's checkbox is a native input, out of sight inside the box a
    // reader sees. A control that `toggles` is pressed twice, so it ends as it started and only focus is left to differ.
    private sealed record Case(string Name, string[] Flux, string[] Rask, bool Opens = false, bool Toggles = false);

    private sealed record Step(string Engine, string Scheme, Case Test, string Mode);

    private sealed record Result(Step Step, JsonObject Theirs, JsonObject Mine, List<string> Diffs);

    private static readonly Case[] Cases =
    [
        new("button", ["components/button", "[data-preview-wrapper] [data-flux-button]"], ["button.html", "[data-ui-button]"]),
        new("input", ["components/input", "[data-preview-wrapper] input[data-flux-control]"], ["input.html", "input[data-ui-control]"]),
        new("textarea", ["components/textarea", "[data-preview-wrapper] textarea"], ["textarea.html", "textarea"]),
        new("select (native)", ["components/select", "[data-preview-wrapper] select"], ["select.html", "select"]),
        new("select (listbox)", ["components/select", "[data-preview-wrapper] [data-flux-select-button]"], ["select.html", "[data-ui-select-button]"], Opens: true),
        new("checkbox", ["components/checkbox", "[data-preview-wrapper] [data-flux-checkbox]"], ["checkbox.html", "input[type=checkbox]", "[data-ui-checkbox]"], Toggles: true),
        new("radio", ["components/radio", "[data-preview-wrapper] [data-flux-radio]"], ["radio.html", "input[type=radio]", "[data-ui-radio]"], Toggles: true),
        new("switch", ["components/switch", "[data-preview-wrapper] [data-flux-switch]"], ["switch.html", "input[role=switch]", "[data-ui-switch]"], Toggles: true),
        new("tabs", ["components/tabs", "[data-preview-wrapper] [data-flux-tab]"], ["tabs.html", "[data-ui-tab]"]),
        new("navlist item", ["demo/sidebar", "[data-flux-sidebar-item]:not([data-current])"], ["sidebar/sidebar.html", "[data-ui-sidebar-item]:not([aria-current])"]),
        new("dropdown trigger", ["components/dropdown", "[data-preview-wrapper] [data-flux-button][aria-haspopup]"], ["dropdown.html", "[data-ui-button][aria-haspopup]"], Opens: true),
        new("date picker", ["components/date-picker", "[data-preview-wrapper] [data-flux-date-picker-button]"], ["date-picker.html", "[data-ui-date-picker-button]"], Opens: true),
        new("date picker (input)", ["components/date-picker", "[data-preview-wrapper] [data-flux-month-input]"], ["date-picker.html", "[data-ui-month-input]"]),
    ];

    // What focus can draw, read off every element of the page before and after the move.
    private static readonly string[] Ring = ["outlineStyle", "outlineWidth", "outlineColor", "outlineOffset", "boxShadow", "borderTopColor"];

    // Where focus LANDS differs here, with nothing drawn on either page: Safari's own rule for a pressed native control,
    // which no stylesheet changes. By engine, case and move, with why; printed as `ok*`, and failed again the moment
    // either side draws a ring.
    private const string SafariPress = "Safari does not focus a native control a mouse presses; Flux's is a custom element with a "
        + "tabindex (or its script focuses it), which any engine focuses. Neither draws a ring.";

    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        ["webkit select (listbox) mouse"] = SafariPress,
        ["webkit checkbox mouse"] = SafariPress,
        ["webkit radio mouse"] = SafariPress,
        ["webkit switch mouse"] = SafariPress,
        ["webkit dropdown trigger mouse"] = "Both menus take the focus as they open, the kit's by `autofocus`, which WebKit counts as "
            + ":focus-visible, Flux's by script, which after a press it does not. The menu draws no ring either way.",
    };

    // Runs in the page: every element's ring properties before the move.
    private const string Remember = """
        (RING) => {
          window.__focusBefore = new WeakMap();
          for (const el of document.querySelectorAll('*')) {
            const style = getComputedStyle(el);
            window.__focusBefore.set(el, Object.fromEntries(RING.map(k => [k, style[k]])));
          }
        }
        """;

    // Runs in the page: where focus is, and every ring the move drew.
    private const string Drawn = """
        (control, RING) => {
          const active = document.activeElement;
          // Drawn at all: a box bigger than the 1px an `sr-only` control is clipped to, and nothing over it transparent.
          const visible = el => {
            const box = el.getBoundingClientRect();
            const style = getComputedStyle(el);
            if (box.width <= 1 || box.height <= 1 || style.visibility === 'hidden' || style.display === 'none') return false;
            for (let up = el; up; up = up.parentElement) if (getComputedStyle(up).opacity === '0') return false;
            return true;
          };
          const shown = style => style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
          // What a marker names on either page: data-flux-x and data-ui-x are the same part.
          const named = el => {
            const marker = [...el.attributes].map(a => a.name).find(n => /^data-(flux|ui)-/.test(n));
            return marker ? marker.replace(/^data-(flux|ui)-/, '') : el.localName;
          };
          const popup = active?.closest(':popover-open, dialog[open]');
          const where = !active || active === document.body ? 'page'
            : active === control ? 'control'
              : control.contains(active) || active.contains(control) ? 'control part'
                : popup ? `popup ${named(popup)}${popup === active ? '' : ` > ${active.getAttribute('role') ?? active.localName}`}`
                  : `elsewhere ${named(active)}`;
          // A shadow drawn at all: Tailwind's ring utilities leave zero-width layers behind, which paint nothing.
          const painted = shadow => {
            const layers = [];
            let depth = 0;
            let layer = '';
            for (const ch of shadow) {
              depth += ch === '(' ? 1 : ch === ')' ? -1 : 0;
              if (ch === ',' && depth === 0) { layers.push(layer.trim()); layer = ''; } else layer += ch;
            }
            layers.push(layer.trim());
            // A layer paints when it has a colour that is not transparent and any blur or spread or offset.
            return layers.filter(l => l !== 'none' && !/rgba\([^)]*,\s*0\)/.test(l)
              && l.replace(/\([^)]*\)/g, '').match(/-?[\d.]+px/g)?.some(n => parseFloat(n) !== 0)).join(', ') || 'none';
          };
          // What the move could have drawn on: the control's own example (the docs page's chrome is not it), whatever
          // popup is open, and every box around where focus is.
          const scope = new Set([
            ...(control.closest('[data-preview-wrapper]') ?? document.body).querySelectorAll('*'),
            ...[...document.querySelectorAll(':popover-open, dialog[open]')].flatMap(popup => [popup, ...popup.querySelectorAll('*')]),
          ]);
          for (let up = active; up && up !== document.body; up = up.parentElement) scope.add(up);
          const rings = [];
          for (const el of scope) {
            if (!visible(el)) continue;
            const style = getComputedStyle(el);
            const before = window.__focusBefore.get(el);
            // An element the move put on the page (a popup's rows) is a ring only by its outline: its shadow and
            // border are its resting look.
            const outline = shown(style) && (!before || RING.slice(0, 4).some(k => before[k] !== style[k]));
            const shadow = Boolean(before) && painted(style.boxShadow) !== painted(before.boxShadow);
            const border = Boolean(before) && style.borderTopColor !== before.borderTopColor;
            if (!outline && !shadow && !border) continue;
            const box = el.getBoundingClientRect();
            rings.push({
              on: el === active ? 'focused' : el.contains(active) ? 'around focused' : named(el),
              outline: outline ? `${style.outlineStyle} ${style.outlineWidth} ${style.outlineColor} offset ${style.outlineOffset}` : 'none',
              boxShadow: shadow ? painted(style.boxShadow) : '(unchanged)',
              border: border ? style.borderTopColor : '(unchanged)',
              size: `${Math.round(box.width)}x${Math.round(box.height)}`,
            });
          }

          // The accent, resolved to the colour the browser writes: what an accent ring is drawn in (Departed.FocusAccent).
          const probe = document.body.appendChild(document.createElement('i'));
          probe.style.color = 'var(--color-accent)';
          const accent = getComputedStyle(probe).color;
          probe.remove();
          return { where, focusVisible: Boolean(active && active !== document.body && active.matches(':focus-visible')), rings, accent };
        }
        """;

    private string? shots;
    private readonly Dictionary<string, IBrowser> browsers = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var engines = new List<(string Name, Func<Task<IBrowser>> Launch)> { ("chromium", () => Browsers.Chromium()) };
        if (arg.Flag("webkit"))
        {
            var safari = await Browsers.WebKit();
            if (safari is null)
            {
                Console.WriteLine("flux parity: no WebKit is installed (flux install webkit) — Chromium alone.\n");
            }
            else
            {
                engines.Add(("webkit", () => safari.LaunchAsync()));
            }
        }

        shots = arg.Option("shots");
        var only = arg.Option("only");
        // Every step is a page of its own on each side, so the steps run side by side: RASK_FLUX_JOBS at once (4).
        var jobs = int.Parse(Environment.GetEnvironmentVariable("RASK_FLUX_JOBS") ?? "4", CultureInfo.InvariantCulture);
        foreach (var (name, launch) in engines)
        {
            browsers[name] = await launch();
        }

        var steps = engines.SelectMany(engine => new[] { "light", "dark" }.SelectMany(scheme => Cases
            .Where(test => only is null || test.Name == only)
            .SelectMany(test => new[] { "mouse", "keyboard" }.Concat(test.Opens ? ["open"] : []).Select(mode => new Step(engine.Name, scheme, test, mode))))).ToList();
        var results = new Result[steps.Count];
        var next = -1;
        await Task.WhenAll(Enumerable.Range(0, Math.Min(jobs, steps.Count)).Select(async _ =>
        {
            for (var at = Interlocked.Increment(ref next); at < steps.Count; at = Interlocked.Increment(ref next))
            {
                var step = steps[at];
                var both = await Task.WhenAll(new[] { "flux", "rask" }.Select(side => Walk(step, side)));
                var (theirs, mine) = (both[0], both[1]);
                var diffs = Departed.FocusAccent.Contains(step.Test.Name) && Rings(theirs).Count > 0 ? Accented(theirs, mine) : CompareRings(theirs, mine, step.Engine);
                results[at] = new Result(step, theirs, mine, diffs);
            }
        }));
        foreach (var browser in browsers.Values)
        {
            await browser.CloseAsync();
        }

        var failures = 0;
        foreach (var (step, theirs, mine, diffs) in results)
        {
            // Accepted only while neither side draws anything: a ring that appears fails it again.
            var accepted = Rings(theirs).Count == 0 && Rings(mine).Count == 0 ? Accepted.GetValueOrDefault($"{step.Engine} {step.Test.Name} {step.Mode}") : null;
            var failed = diffs.Count > 0 && accepted is null;
            failures += failed ? 1 : 0;
            Console.WriteLine($"{(failed ? "FAIL" : diffs.Count > 0 ? "ok* " : "ok  ")} {step.Engine,-8} {step.Scheme,-5} {step.Test.Name,-20} {step.Mode,-8} "
                + $"flux: {Describe(theirs)} · rask: {Describe(mine)}");
            if (diffs.Count > 0 && accepted is not null)
            {
                Console.WriteLine($"       accepted: {accepted}");
            }

            foreach (var d in diffs.Take(arg.Flag("all") ? int.MaxValue : 6))
            {
                Console.WriteLine($"       {d}");
            }
        }

        Console.WriteLine(failures > 0 ? $"\nflux parity: focus draws differently in {failures} step(s)." : "\nflux parity: focus draws what Flux's draws.");
        return failures > 0 ? 1 : 0;
    }

    // One control moved one way on one side, in a page of its own: what focus is on, and every ring the move drew.
    private async Task<JsonObject> Walk(Step step, string side)
    {
        var (test, mode) = (step.Test, step.Mode);
        var context = await browsers[step.Engine].NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            ColorScheme = step.Scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        var page = await context.NewPageAsync();
        var parts = side == "flux" ? test.Flux : test.Rask;
        var (where, selector, pressed) = (parts[0], parts[1], parts.Length > 2 ? parts[2] : null);
        var file = Repo.Parity("rask", where);
        if (side == "rask" && !File.Exists(file))
        {
            throw new FluxExit(1, $"flux parity: {file} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
        }

        var url = side == "flux" ? $"https://fluxui.dev/{where}" : Repo.FileUrl(file);
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        if (side == "rask")
        {
            await Runtime.With(page, "rask-hooks.ts");
        }

        await page.Run("() => document.fonts.ready.then(() => undefined)");
        var control = page.Locator(selector).First;
        var target = pressed is not null ? page.Locator(pressed).First : control;
        await target.Run("el => el.scrollIntoView({ block: 'center', behavior: 'instant' })");
        await page.Mouse.MoveAsync(2, 2);
        await Settle(page);
        await page.Run(Remember, Ring);

        if (mode == "mouse")
        {
            var box = (await target.BoundingBoxAsync())!;
            await page.Mouse.ClickAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            if (test.Toggles)
            {
                await page.WaitForTimeoutAsync(400);
                await page.Mouse.ClickAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            }
        }
        else
        {
            // The keyboard as the last thing used, then focus on the control: what a Tab onto it leaves.
            await page.Keyboard.PressAsync("Tab");
            await control.FocusAsync();
            if (mode == "open")
            {
                await page.Keyboard.PressAsync("Enter");
            }
        }

        // Away from it, so a hover is not mistaken for focus.
        await page.Mouse.MoveAsync(2, 2);
        await Settle(page);
        var measured = (JsonObject)(await control.Run(Drawn, Ring))!;
        if (shots is not null)
        {
            var dir = Path.Combine(shots, step.Engine, step.Scheme);
            Directory.CreateDirectory(dir);
            var name = Trailing().Replace(Separators().Replace(test.Name, "-"), "");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{name}-{mode}-{side}.png") });
        }

        await context.CloseAsync();
        return measured;
    }

    // Two frames and a transition's length: what a move started has finished drawing.
    private static async Task Settle(IPage page)
    {
        await page.WaitForTimeoutAsync(250);
        await page.Run("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
    }

    private static List<JsonObject> Rings(JsonObject m) => (m["rings"] as JsonArray ?? []).OfType<JsonObject>().ToList();

    private static string S(JsonObject o, string key) => Js.Str(o[key]) ?? "";

    private static string Describe(JsonObject m)
    {
        var rings = string.Join(", ", Rings(m).Select(r => (S(r, "outline") != "none" ? $"outline {string.Join(' ', S(r, "outline").Split(' ').Take(2))}" : "")
            + (S(r, "boxShadow") != "(unchanged)" ? " shadow" : "") + (S(r, "border") != "(unchanged)" ? " border" : "") + $" on {S(r, "on")} {S(r, "size")}"));
        return $"{S(m, "where")}{(Js.Truthy(m["focusVisible"]) ? " :focus-visible" : "")}{(rings.Length > 0 ? $" [{rings.Trim()}]" : " [no ring]")}";
    }

    private static string Kind(string where) => ControlPart().Replace(Popup().Replace(where, ""), "control");

    private static void Placed(JsonObject flux, JsonObject mine, List<string> diffs)
    {
        if (Kind(S(flux, "where")) != Kind(S(mine, "where")))
        {
            diffs.Add($"focus is on: flux {S(flux, "where")} · rask {S(mine, "where")}");
        }

        var (a, b) = (Js.Truthy(flux["focusVisible"]), Js.Truthy(mine["focusVisible"]));
        if (a != b)
        {
            diffs.Add($":focus-visible: flux {(a ? "true" : "false")} · rask {(b ? "true" : "false")}");
        }
    }

    // A FOCUSED FIELD (Departed.FocusAccent): where Flux's field draws its outline, the kit's draws the accent — no
    // outline of the browser's anywhere, the field's border and an inset 1px ring in the accent, and nothing else drawn
    // in any other colour. Where focus is, and :focus-visible, are still Flux's.
    private static List<string> Accented(JsonObject flux, JsonObject mine)
    {
        var diffs = new List<string>();
        Placed(flux, mine, diffs);
        var accent = S(mine, "accent");
        var ring = $"{accent} 0px 0px 0px 1px inset";
        if (!Rings(mine).Any(r => S(r, "border") == accent && S(r, "boxShadow") == ring))
        {
            diffs.Add($"no accent ring (border and {ring})");
        }

        foreach (var r in Rings(mine))
        {
            var outline = S(r, "outline") == "none" || S(r, "outline").StartsWith($"solid 1px {accent} ", StringComparison.Ordinal);
            var shadow = S(r, "boxShadow") == "(unchanged)" || S(r, "boxShadow") == ring;
            var border = S(r, "border") == "(unchanged)" || S(r, "border") == accent;
            if (!outline || !shadow || !border)
            {
                diffs.Add($"not the accent ring on {S(r, "on")}: {S(r, "outline")} | {S(r, "boxShadow")} | {S(r, "border")}");
            }
        }

        return diffs;
    }

    // What differs: where focus is, :focus-visible, and the rings, by what each draws (not by the element that draws
    // it: Flux's checkbox is a custom element where the kit's is a native input inside its box). WebKit draws an `auto`
    // outline as the platform's focus ring, whatever width and colour it computes (the kit's checkbox ring computes 1px
    // and the focus-ring colour where the browser's own computes 3px and currentcolor, and the two are the same
    // pixels), so there only its style and offset are compared.
    private static List<string> CompareRings(JsonObject flux, JsonObject mine, string engine)
    {
        var diffs = new List<string>();
        Placed(flux, mine, diffs);
        string Outline(string o) => engine == "webkit" && o.StartsWith("auto ", StringComparison.Ordinal) ? $"auto {o[o.IndexOf(" offset ", StringComparison.Ordinal)..]}" : o;
        string Key(JsonObject r) => $"{Outline(S(r, "outline"))} | {S(r, "boxShadow")} | {S(r, "border")}";
        var theirs = Rings(flux).Select(Key).ToList();
        var ours = Rings(mine).Select(Key).ToList();
        foreach (var ring in theirs.Concat(ours).Distinct(StringComparer.Ordinal))
        {
            var (a, b) = (theirs.Count(k => k == ring), ours.Count(k => k == ring));
            if (a != b)
            {
                diffs.Add($"ring {ring}: flux {a} · rask {b}");
            }
        }

        return diffs;
    }

    [GeneratedRegex("[ ()]+")]
    private static partial Regex Separators();

    [GeneratedRegex("-$")]
    private static partial Regex Trailing();

    [GeneratedRegex(" > .*")]
    private static partial Regex Popup();

    [GeneratedRegex("^control part$")]
    private static partial Regex ControlPart();
}
