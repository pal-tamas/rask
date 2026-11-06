using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>Holds a Rask.Ui menu to Flux UI's live documentation while it is OPEN.</summary>
/// <remarks>
///     <c>parity</c> measures a page as it loads, and a menu loads closed: the component is the part it never sees. This
///     opens every example the same way on both pages — a click on the trigger of a dropdown, a right-click on the target
///     of a context menu — and compares what is then on screen:
///     <list type="bullet">
///         <item>that it opened, where focus went, and what the page behind it was given (scroll lock, no pointer);</item>
///         <item>the popup: where it sits against its trigger (or the pointer), its box and every row's computed styles, pseudo-elements and glyphs;</item>
///         <item>each row hovered by a real pointer, pressed, and under the keyboard cursor;</item>
///         <item>
///             the POINTER'S OWN WALK: which rows are lit (<c>data-active</c>) and where focus is with the pointer on each
///             row, and after it has left the menu; and on the diagonal from a submenu's row to the far corner of its
///             flyout, step by step, that the flyout is still showing and no row below was lit on the way; and from that
///             row straight out of the menu, away from its flyout, a move at a time: when the flyout closes, what is lit;
///         </item>
///         <item>
///             THE POINTER AND THE KEYS TOGETHER: the pointer lights a row, the first arrow lands on it, the next goes on,
///             the pointer lights another while focus stays, the arrow after counts from the lit row, and the keyboard's
///             row stays lit when the pointer leaves — lit rows and the focused row after every step;
///         </item>
///         <item>the first submenu's flyout, opened by hovering its row: where it sits against the row, and its rows;</item>
///         <item>that Escape and a click outside close it, and where Escape leaves focus;</item>
///         <item>
///             THE PAGE BEHIND, in a browser that shows its scrollbars, at 1920 x 1080 and 390 x 844: <c>scrollY</c> and
///             the example's <c>x</c> and <c>width</c> before, while and after the menu is open, and the three declarations
///             on <c>&lt;html&gt;</c>. (The browser the rest runs in hides its scrollbars, as every headless one does, and
///             there the kit keeps no gutter: <c>scrollbar-gutter</c> is compared here, where there is a scrollbar to keep
///             one for — one that TAKES ROOM: macOS draws its scrollbars over the page even when asked to show them, and
///             there Flux's <c>stable</c> reserves nothing and the kit asks for nothing.)
///         </item>
///         <item>
///             WHERE IT LANDS with no room: every menu opened with its trigger — or the pointer — held at the edges of a
///             phone and of a desktop, and with more rows than fit (<see cref="Placement" />).
///         </item>
///     </list>
///     <para>
///         ON A PHONE (<c>--view 390x844</c>) the same walk is taken with a finger, as Flux's page answers one: a tap on a
///         dropdown's trigger, a LONG PRESS on a context menu's target (the browser's <c>contextmenu</c>, as a
///         right-click's; a tap opens nothing), and every row TAPPED in a menu opened afresh: open or closed after it, lit
///         rows, focus, a flyout. A submenu's flyout is opened by a tap on its row, and that row is tapped again. A finger
///         has no pointer to rest on a row, so the hover, the pointer's walk, the diagonal and the pointer with the keys
///         are not taken there; the keys are, for a phone with a keyboard. The page behind and the landings are the
///         desktop run's: both measure a phone already.
///     </para>
///     <para>
///         A Rask parity page is static HTML: the kit's sheet, and the runtime's behaviour hooks added as one script
///         (<see cref="Runtime" />) — the cursor, the pointer's lit row, the open flyout, the safe area and the page lock
///         are theirs, by attribute, and none of it is C#'s. Everything else above is the platform's —
///         <c>popovertarget</c>, the top layer, light dismiss. So the same keys and the same pointer drive both pages.
///         What IS the page's is not here and is proved by the unit tests: <c>aria-expanded</c>, and what a press on a
///         row runs.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux parity-menu dropdown
///     flux parity-menu context --refresh --all                     # re-measure Flux, every difference
///     flux parity-menu dropdown --view 390x844                     # on a phone
///     </code>
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("parity-menu", "an open dropdown or context menu, its walks and the page behind, against Flux's")]
public sealed partial class ParityMenu : IFluxCommand
{
    // Flux's custom elements, and the native element Rask.Ui writes in their place.
    private static readonly Dictionary<string, string> Native = new(StringComparer.Ordinal)
    {
        ["ui-menu"] = "div",
        ["ui-submenu"] = "div",
        ["ui-menu-radio-group"] = "div",
        ["ui-menu-checkbox-group"] = "div",
        ["ui-menu-radio"] = "button",
        ["ui-menu-checkbox"] = "button",
    };

    // What is not compared, and why. `width`/`height` are the box's, already compared. On the popup itself: Flux places
    // it with script — `position: absolute` at page coordinates, in a `popover="manual"` it dismisses itself — and Rask
    // with the platform — a `popover="auto"` in the top layer, `position: fixed`, CSS anchor positioning and the margins
    // that carry its gap. Where it ENDS UP is compared, as placement.
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "width", "height" };
    private static readonly HashSet<string> Placed = new(StringComparer.Ordinal) { "position", "marginTop", "marginRight", "marginBottom", "marginLeft", "transform", "zIndex" };

    // What a row lets go by the NATIVE FEEL departure (DepartedFrom).
    private static readonly string[] RowFeel = ["userSelect", "webkitUserSelect", "touchAction", "webkitTouchCallout"];

    private static readonly string[] Inherited = ["color", "fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing"];

    // What the page behind an open menu is given. The gutter is compared where there is one: MeasureBehind.
    private static readonly string[] Lock = ["overflowY", "pointerEvents"];
    private static readonly (string Name, int Width, int Height)[] Viewports = [("1920x1080", 1920, 1080), ("390x844", 390, 844)];

    // Runs in the page. What the runtime's right-click hook does (rask-dom.ts, "Context menus"), which a static parity
    // page does not load: the pointer's position on <html>, then the popover the area names.
    private const string ShowAtPointer = """
        ([x, y]) => {
          const area = document.querySelector('[data-pm="trigger"]').closest('[data-rask-contextmenu]');
          document.documentElement.style.setProperty('--rask-context-x', x + 'px');
          document.documentElement.style.setProperty('--rask-context-y', y + 'px');
          document.getElementById(area.getAttribute('data-rask-contextmenu')).showPopover();
        }
        """;

    // Runs in the page. Names the example's trigger and popup.
    private const string MarkScript = """
        (wrapper, { prefix, index, INHERITED }) => {
          const headings = [...document.querySelectorAll('h2[id]')];
          const section = wrapper.dataset.section
            ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
          const computed = getComputedStyle(wrapper);
          const inherited = Object.fromEntries(INHERITED.map(k => [k, computed[k]]));
          const host = wrapper.querySelector(`[${prefix}dropdown], [${prefix}context]`);
          if (!host) {
            return { section, inherited, kind: null, index };
          }

          host.firstElementChild.setAttribute('data-pm', 'trigger');
          host.querySelector('[popover]').setAttribute('data-pm', 'panel');
          return { section, inherited, kind: host.hasAttribute(`${prefix}context`) ? 'context' : 'dropdown' };
        }
        """;

    // Runs in the page. The open popup: every element in it, measured from the trigger (or the pointer).
    private const string Collect = """
        ({ STYLES, PRESSABLE, LOCK, kind, point }) => {
          const panel = document.querySelector('[data-pm="panel"]');
          const trigger = document.querySelector('[data-pm="trigger"]');
          const opened = panel.matches(':popover-open');
          const html = getComputedStyle(document.documentElement);
          const lock = Object.fromEntries(LOCK.map(k => [k, html[k]]));
          lock.panelPointerEvents = getComputedStyle(panel).pointerEvents;
          const active = document.activeElement;
          const focus = active === panel ? 'menu' : active === trigger ? 'trigger' : panel.contains(active) ? 'row' : 'elsewhere';
          if (!opened) {
            return { opened, focus, lock, nodes: [], rows: [], submenu: null };
          }

          const anchor = trigger.getBoundingClientRect();
          const origin = kind === 'context' ? { x: point[0], y: point[1] } : anchor;
          const { nodes, rows } = tree(panel, origin, STYLES, PRESSABLE);
          const sub = panel.querySelector('[data-flux-menu-submenu] > button, [data-ui-menu-submenu] > button');
          window.__pmBase = Object.fromEntries(nodes.map(n => [n.id, n.style]));
          return { opened, focus, lock, nodes, rows, submenu: sub ? Number(sub.getAttribute('data-pm-n')) : null };

          function tree(top, from, styles, pressable) {
            const keep = name => /^(role|aria-checked|aria-disabled|disabled|data-checked|popover)$/.test(name);
            const list = [];
            const picked = [];
            const next = document.querySelectorAll('[data-pm-n]').length;
            [top, ...top.querySelectorAll('*')].forEach((el, i) => {
              const id = next + i;
              el.setAttribute('data-pm-n', id);
              const box = el.getBoundingClientRect();
              const computed = getComputedStyle(el);
              const node = {
                id, parent: el === top ? null : Number(el.parentElement.getAttribute('data-pm-n')), tag: el.tagName.toLowerCase(),
                attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
                text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
                box: [box.x - from.x, box.y - from.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
                style: Object.fromEntries(styles.map(k => [k, computed[k]])),
                pressable: !!el.closest(pressable),
              };
              for (const pseudo of ['::before', '::after']) {
                const drawn = getComputedStyle(el, pseudo);
                if (drawn.content && drawn.content !== 'none' && drawn.content !== 'normal') {
                  node[pseudo] = { content: drawn.content, ...Object.fromEntries(styles.map(k => [k, drawn[k]])) };
                }
              }

              list.push(node);
              // A row someone can point at: an item of any kind, or a navigation menu's link, that is on screen.
              if (el.matches('[role^=menuitem], a') && box.width > 0) {
                picked.push(id);
              }
            });
            return { nodes: list, rows: picked };
          }
        }
        """;

    // Runs in the page. What changed in the popup's own rows since it was measured at rest. A submenu's flyout is left
    // out — hovering its row opens it, and it is measured on its own — and so is anything with no box.
    private const string Diff = """
        ({ STYLES }) => {
          const changed = {};
          const panel = document.querySelector('[data-pm="panel"]');
          for (const [id, base] of Object.entries(window.__pmBase)) {
            const el = document.querySelector(`[data-pm-n="${id}"]`);
            if (!el || el.getClientRects().length === 0 || (el !== panel && el.closest('[role=menu], nav') !== panel)) continue;
            const computed = getComputedStyle(el);
            for (const k of STYLES) {
              if (computed[k] !== base[k]) (changed[id] ??= {})[k] = computed[k];
            }
          }

          return changed;
        }
        """;

    // Runs in the page. Which of the popup's own rows are lit, by position, and where focus is.
    private const string Lit = """
        (rows) => {
          const panel = document.querySelector('[data-pm="panel"]');
          const active = document.activeElement;
          return {
            lit: rows.map((row, i) => document.querySelector(`[data-pm-n="${row}"]`).hasAttribute('data-active') ? i : -1).filter(i => i >= 0),
            focus: active === panel ? 'menu' : panel.contains(active) ? 'row' : 'elsewhere',
          };
        }
        """;

    // Runs in the page. The lit rows and the focused row, by position among the popup's own rows.
    private const string Where = """
        (rows) => {
          const at = el => rows.findIndex(row => document.querySelector(`[data-pm-n="${row}"]`) === el);
          return {
            lit: rows.map((row, i) => (document.querySelector(`[data-pm-n="${row}"]`).hasAttribute('data-active') ? i : -1)).filter(i => i >= 0),
            focus: at(document.activeElement),
          };
        }
        """;

    // Runs in the page. After a tap: is the popup open, which of its own rows are lit, where focus is (a row by its
    // position), and how many flyouts are showing.
    private const string Tapped = """
        (rows) => {
          const panel = document.querySelector('[data-pm="panel"]');
          const active = document.activeElement;
          const at = rows.findIndex(row => document.querySelector(`[data-pm-n="${row}"]`) === active);
          const where = active === panel ? 'menu' : active === document.querySelector('[data-pm="trigger"]') ? 'trigger' : active === document.body ? 'body' : 'elsewhere';
          const open = panel.matches(':popover-open');
          // A closed menu's lit row is seen by nobody: Flux leaves the tapped one lit, the kit does not.
          return {
            open,
            lit: !open ? [] : rows.map((row, i) => (document.querySelector(`[data-pm-n="${row}"]`).hasAttribute('data-active') ? i : -1)).filter(i => i >= 0),
            focus: at >= 0 ? at : where,
            flyouts: [...panel.querySelectorAll('[role=menu]')].filter(menu => menu.getClientRects().length > 0).length,
          };
        }
        """;

    // Runs in the page. On the way from a submenu's row to its flyout: is the flyout still there, and which of the
    // popup's own rows are lit — the submenu's, until the pointer is inside the flyout, and never one it crossed.
    private const string Crossing = """
        ({ row, rows }) => {
          const trigger = document.querySelector(`[data-pm-n="${row}"]`);
          const flyout = trigger.parentElement.querySelector('[role=menu]');
          return {
            flyout: flyout.getClientRects().length > 0,
            lit: rows.map((id, i) => document.querySelector(`[data-pm-n="${id}"]`).hasAttribute('data-active') ? i : -1)
              .filter(i => i >= 0 && rows[i] !== row),
          };
        }
        """;

    // Runs in the page. The flyout a submenu row opened, measured from that row.
    private const string CollectFlyout = """
        ({ STYLES, PRESSABLE, row }) => {
          const trigger = document.querySelector(`[data-pm-n="${row}"]`);
          const flyout = trigger.parentElement.querySelector('[role=menu]');
          const from = trigger.getBoundingClientRect();
          const keep = name => /^(role|aria-checked|aria-disabled|disabled|data-checked)$/.test(name);
          const ids = new Map();
          return [flyout, ...flyout.querySelectorAll('*')].map((el, i) => {
            ids.set(el, i);
            const box = el.getBoundingClientRect();
            const computed = getComputedStyle(el);
            return {
              id: i, parent: el === flyout ? null : ids.get(el.parentElement), tag: el.tagName.toLowerCase(),
              attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
              text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
              box: [box.x - from.x, box.y - from.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
              style: Object.fromEntries(STYLES.map(k => [k, computed[k]])),
              pressable: !!el.closest(PRESSABLE),
            };
          });
        }
        """;

    // Runs in the page. Whether the popup is gone, and where focus was left.
    private const string Closed = """
        () => {
          const panel = document.querySelector('[data-pm="panel"]');
          const active = document.activeElement;
          return { closed: !panel.matches(':popover-open'), focus: active === document.querySelector('[data-pm="trigger"]') ? 'trigger' : active === document.body ? 'body' : 'elsewhere' };
        }
        """;

    // Runs in the page.
    private const string Unmark = """
        () => {
          document.querySelectorAll('[data-pm]').forEach(el => el.removeAttribute('data-pm'));
          document.querySelectorAll('[data-pm-n]').forEach(el => el.removeAttribute('data-pm-n'));
          delete window.__pmBase;
        }
        """;

    private IBrowser browser = null!;
    private View view = null!;
    private string slug = "";

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        if (arg.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: flux parity-menu <dropdown|context> [--refresh] [--all] [--view 390x844]");
            return 1;
        }

        slug = arg.Positional[0];
        var raskPage = Repo.Parity("rask", $"{slug}.html");
        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        view = View.Of(arg);
        browser = await Browsers.Chromium();
        var fluxUrl = $"https://fluxui.dev/components/{slug}";
        var fluxFile = Repo.Parity("flux", slug, $"open{view.Sized}.json");
        JsonObject flux;
        if (File.Exists(fluxFile) && !arg.Flag("refresh"))
        {
            flux = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(fluxFile))!;
        }
        else
        {
            flux = await MeasureOpen(fluxUrl, "data-flux-", null);
            flux["behind"] = view.Sized.Length > 0 ? new JsonObject() : await MeasureBehind(fluxUrl, "data-flux-");
            Directory.CreateDirectory(Repo.Parity("flux", slug));
            await File.WriteAllTextAsync(fluxFile, Js.Stringify(flux));
        }

        if (flux["behind"] is null)
        {
            Console.Error.WriteLine($"flux parity: {fluxFile} was measured before the page behind was — run with --refresh");
            return 1;
        }

        var rask = await MeasureOpen(Repo.FileUrl(raskPage), "data-ui-", flux);
        rask["behind"] = view.Sized.Length > 0 ? new JsonObject() : await MeasureBehind(Repo.FileUrl(raskPage), "data-ui-");
        var astray = 0;
        if (view.Sized.Length == 0)
        {
            var hosts = await HostsOf(fluxUrl);
            astray = await Placement.Landings(browser, new Placement.Family(slug,
                new Placement.Side(fluxUrl, $"[data-preview-wrapper] [data-flux-{slug}]", "[popover]", null),
                new Placement.Side(Repo.FileUrl(raskPage), $"[data-preview-wrapper] [data-ui-{slug}]", "[popover]", null, slug == "context" ? AtPointer : null),
                [.. hosts.Select(index => slug == "context" ? new Placement.Host(index, "context") : new Placement.Host(index))])
            {
                Refresh = arg.Flag("refresh"),
                All = arg.Flag("all"),
            });
        }

        await browser.CloseAsync();
        if (arg.Flag("dump"))
        {
            await File.WriteAllTextAsync(Repo.Parity("rask", $"{slug}.open.json"), Js.Stringify(rask));
        }

        var limit = arg.Flag("all") ? int.MaxValue : 12;
        var failures = 0;
        foreach (var scheme in new[] { "light", "dark" })
        {
            var theirsList = (flux[scheme] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var mineList = (rask[scheme] as JsonArray ?? []).OfType<JsonObject>().ToList();
            for (var index = 0; index < theirsList.Count; index++)
            {
                var theirs = theirsList[index];
                var mine = index < mineList.Count ? mineList[index] : null;
                var section = Js.Str(theirs["section"]) is { Length: > 0 } s ? s : "intro";
                var label = $"{scheme}{view.Sized} {section}#{index}";
                var diffs = mine is not null ? CompareExample(theirs, mine) : ["Rask has no such example"];
                failures += diffs.Count > 0 ? 1 : 0;
                var rows = (theirs["rows"] as JsonArray ?? []).Count;
                Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {label}{(diffs.Count > 0 ? $" — {diffs.Count} difference(s)" : $" — {rows} rows{(Js.Truthy(theirs["flyout"]) ? ", flyout" : "")}")}");
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

        foreach (var (size, theirsNode) in (JsonObject)flux["behind"]!)
        {
            var theirs = (JsonObject)theirsNode!;
            var mine = rask["behind"]![size] as JsonObject ?? [];
            // A gutter is kept where a scrollbar took room. Where none does, Flux's `scrollbar-gutter: stable` holds nothing
            // either, and the kit does not write it (rask-lock.ts: on a page that shows no scrollbar anywhere else it would
            // narrow the page by one). The other two declarations are compared everywhere.
            var any = Js.Truthy(theirs["scrollbar"]) || Js.Truthy(mine["scrollbar"]);
            JsonObject Kept(JsonObject side)
            {
                if (any)
                {
                    return side;
                }

                var copy = (JsonObject)side.DeepClone();
                copy["lock"] = string.Join(" | ", (Js.Str(side["lock"]) ?? "").Split(" | ").Take(2));
                return copy;
            }

            var (keptTheirs, keptMine) = (Kept(theirs), Kept(mine));
            var diffs = theirs.Select(p => p.Key).Where(key => JsString.Json(keptTheirs, key) != JsString.Json(keptMine, key))
                .Select(key => $"{key}: {JsString.Json(theirs, key)} vs {JsString.Json(mine, key)}").ToList();
            failures += diffs.Count > 0 ? 1 : 0;
            Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} the page behind, {size}, scrollbars showing{(diffs.Count > 0 ? "" : $" — {JsString.Of(theirs, "lock")}; moved {JsString.Of(theirs, "moved")}")}");
            foreach (var d in diffs)
            {
                Console.WriteLine($"       {d}");
            }
        }

        Console.WriteLine(failures > 0 ? $"\nflux parity (open): {slug}{view.Sized} differs in {failures} example(s)." : $"\nflux parity (open): {slug}{view.Sized} matches Flux.");
        if (view.Sized.Length == 0)
        {
            Console.WriteLine(astray > 0 ? $"flux parity (open): {slug} lands elsewhere for {astray} menu(s)." : $"flux parity (open): {slug} lands where Flux's does.");
        }

        return failures > 0 || astray > 0 ? 1 : 0;
    }

    // The hosts of Flux's page that hold a menu, by their place among all of them.
    private async Task<List<int>> HostsOf(string url)
    {
        var page = await browser.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        var held = await page.Run("""
            marker => [...document.querySelectorAll(`[data-preview-wrapper] [${marker}]`)]
              .map((el, index) => (el.querySelector('[popover]') ? index : -1)).filter(index => index >= 0)
            """, $"data-flux-{slug}");
        await page.CloseAsync();
        return (held as JsonArray ?? []).Select(n => (int)Js.Number(n)).ToList();
    }

    // What the runtime's right-click hook does (rask-dom.ts, "Context menus"), which a static parity page does not load:
    // the pointer's position on <html>, then the popover the area names.
    private static async Task AtPointer(IPage page, double[] point, string how)
    {
        await page.Mouse.ClickAsync((float)point[0], (float)point[1], new MouseClickOptions { Button = MouseButton.Right });
        await page.Run("""
            ([x, y]) => {
              // A place the held area was moved out of view from: no pointer gets there, on either page.
              const area = document.elementFromPoint(x, y)?.closest('[data-rask-contextmenu]');
              if (!area) return;
              document.documentElement.style.setProperty('--rask-context-x', `${x}px`);
              document.documentElement.style.setProperty('--rask-context-y', `${y}px`);
              document.getElementById(area.getAttribute('data-rask-contextmenu')).showPopover();
            }
            """, point);
    }

    // A finger held on the screen: Chromium makes a `contextmenu` of it, as a phone's browser does — on a Mac. On Linux it
    // makes none (measured 2026-10-30 in Playwright's 1.63 image: pointerdown, touchstart, then a second later pointerup
    // and touchend, and nothing else, three runs of three), so there the `contextmenu` the press would have made is made
    // at its point by the right button — the same event, at the same place, on both pages.
    private static async Task LongPress(IPage page, double x, double y)
    {
        await page.Run("""
            () => {
              window.__parityHeld = false;
              addEventListener('contextmenu', () => { window.__parityHeld = true; }, { capture: true, once: true });
            }
            """);
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.synthesizeTapGesture", new Dictionary<string, object>
        {
            ["x"] = x,
            ["y"] = y,
            ["duration"] = 1000,
            ["tapCount"] = 1,
            ["gestureSourceType"] = "touch",
        });
        await cdp.DetachAsync();
        if (!Js.Truthy(await page.Run("() => window.__parityHeld")))
        {
            await page.Mouse.ClickAsync((float)x, (float)y, new MouseClickOptions { Button = MouseButton.Right });
        }
    }

    // ---- measuring ------------------------------------------------------------------------------------

    private async Task<JsonObject> MeasureOpen(string url, string prefix, JsonObject? like)
    {
        var schemes = new JsonObject();
        foreach (var scheme in new[] { "light", "dark" })
        {
            var options = view.Context();
            options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
            var context = await browser.NewContextAsync(options);
            var page = await context.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            // Every declared face, not only those the closed page uses (as parity-tooltip): the medium a row is set in is
            // first needed when the first menu opens, and was measured in the fallback face while it loaded.
            await page.Run("""
                async () => {
                  await Promise.all([...document.fonts].map(face => face.load().catch(() => {})));
                  await document.fonts.ready;
                  // A link a tap follows takes Flux's page away (it loads again, scrolled to the top). Stopped last, on the
                  // window, once both menus have heard the press: the walk stays on the page.
                  addEventListener('click', e => { if (e.target.closest?.('[popover] a[href]')) e.preventDefault(); });
                }
                """);
            if (like is not null)
            {
                // What Flux does in script the kit asks the runtime for: the lit row, the safe area, the page lock.
                await Runtime.With(page, "rask-hooks.ts");
                // The same surroundings Flux's example had, as parity gives them: only the component differs.
                await page.Run("""
                    ({ examples, INHERITED }) => {
                      // Flux's page goes on below its last example. Without as much room here, the last menu would have
                      // nowhere to open but upward, and that would be the page's doing.
                      document.body.style.paddingBottom = '100vh';
                      document.querySelectorAll('[data-preview-wrapper]').forEach((wrapper, i) => {
                        for (const key of examples[i] ? INHERITED : []) wrapper.style[key] = examples[i].inherited[key];
                      });
                    }
                    """, new JsonObject { ["examples"] = like[scheme]?.DeepClone(), ["INHERITED"] = Js.ToNode(Inherited) });
            }

            var cdp = await context.NewCDPSessionAsync(page);
            await cdp.SendAsync("DOM.enable");
            await cdp.SendAsync("CSS.enable");
            var examples = new JsonArray();
            var count = await page.Locator("[data-preview-wrapper]").CountAsync();
            for (var index = 0; index < count; index++)
            {
                examples.Add(await MeasureExample(page, cdp, index, prefix));
            }

            schemes[scheme] = examples;
            await context.CloseAsync();
        }

        return schemes;
    }

    private static List<int> Ints(JsonNode? node) => (node as JsonArray ?? []).Select(n => (int)Js.Number(n)).ToList();

    private async Task<JsonObject> MeasureExample(IPage page, ICDPSession cdp, int index, string prefix)
    {
        var wrapper = page.Locator("[data-preview-wrapper]").Nth(index);
        async Task Rest()
        {
            await page.Keyboard.PressAsync("Escape");
            if (!view.Phone)
            {
                await page.Mouse.MoveAsync(3, 3);
            }

            await Screen.Press(page, 3, 3, view.Phone);
            await page.WaitForTimeoutAsync(120);
        }

        await Rest();
        await wrapper.Run("el => el.scrollIntoView({ block: 'center' })");

        var parts = (JsonObject)(await wrapper.Run(MarkScript, new JsonObject { ["prefix"] = prefix, ["index"] = index, ["INHERITED"] = Js.ToNode(Inherited) }))!;
        var kind = Js.Str(parts["kind"]);
        var example = new JsonObject
        {
            ["index"] = index,
            ["section"] = parts["section"]?.DeepClone(),
            ["inherited"] = parts["inherited"]?.DeepClone(),
            ["kind"] = kind,
            ["rows"] = new JsonArray(),
            ["states"] = new JsonArray(),
        };
        if (kind is null)
        {
            return example;
        }

        async Task<double[]> Open()
        {
            var box = (await page.Locator("[data-pm=\"trigger\"]").BoundingBoxAsync())!;
            double[] point = [Js.Round(box.X + (box.Width / 2)), Js.Round(box.Y + (box.Height / 2))];
            if (kind == "context" && view.Phone)
            {
                await LongPress(page, point[0], point[1]);
            }
            else if (view.Phone)
            {
                await Screen.Press(page, point[0], point[1], true);
            }
            else
            {
                await page.Mouse.ClickAsync((float)point[0], (float)point[1], new MouseClickOptions { Button = kind == "context" ? MouseButton.Right : MouseButton.Left });
            }

            if (kind == "context" && prefix == "data-ui-")
            {
                // The right-click hook is the runtime's (rask-dom.ts, "Context menus"), which a static parity page does
                // not load. This is what it does: the pointer's position on <html>, then the popover the area names.
                await page.Run(ShowAtPointer, point);
            }

            // A long press opens Flux's menu from the `contextmenu` the browser makes of it, when it makes it: a loaded
            // Linux runner measured the menu shut where it opens a moment later. Waited on, up to two seconds; a menu that
            // does not open by then did not open.
            if (kind == "context" && view.Phone)
            {
                try
                {
                    await page.WaitForFunctionAsync("() => document.querySelector('[data-pm=\"panel\"]')?.matches(':popover-open')", null, new PageWaitForFunctionOptions { Timeout = 2000 });
                }
                catch (TimeoutException)
                {
                    // a menu that does not open by then did not open
                }
            }

            await page.WaitForTimeoutAsync(300);
            return point;
        }

        async Task MoveTo(int row, int? steps = null)
        {
            var box = (await page.Locator($"[data-pm-n=\"{row}\"]").BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), steps is null ? null : new MouseMoveOptions { Steps = steps.Value });
        }

        async Task Reopen()
        {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(150);
            await Open();
        }

        var point = await Open();
        var collected = (JsonObject)(await page.Run(Collect, new { STYLES = Pressables.Styles, PRESSABLE = Pressables.Pressable, LOCK = Lock, kind, point }))!;
        foreach (var (key, value) in collected.ToList())
        {
            example[key] = value?.DeepClone();
        }

        if (!Js.Truthy(example["opened"]))
        {
            await page.Run(Unmark);
            return example;
        }

        var rows = Ints(example["rows"]);
        var rowsArg = Js.ToNode(rows);
        var states = (JsonArray)example["states"]!;
        var styles = new { STYLES = Pressables.Styles };

        // Each row under a real pointer (a finger has none), then pressed.
        foreach (var row in rows)
        {
            if (!view.Phone)
            {
                await MoveTo(row);
                await page.WaitForTimeoutAsync(60);
                states.Add(new JsonObject { ["row"] = row, ["state"] = "hover", ["changed"] = await page.Run(Diff, styles) });
            }

            var doc = await cdp.SendAsync("DOM.getDocument", new Dictionary<string, object> { ["depth"] = 0 });
            var found = await cdp.SendAsync("DOM.querySelector", new Dictionary<string, object>
            {
                ["nodeId"] = doc!.Value.GetProperty("root").GetProperty("nodeId").GetInt32(),
                ["selector"] = $"[data-pm-n=\"{row}\"]",
            });
            var nodeId = found!.Value.GetProperty("nodeId").GetInt32();
            await cdp.SendAsync("CSS.forcePseudoState", new Dictionary<string, object> { ["nodeId"] = nodeId, ["forcedPseudoClasses"] = new[] { "hover", "active" } });
            states.Add(new JsonObject { ["row"] = row, ["state"] = "press", ["changed"] = await page.Run(Diff, styles) });
            await cdp.SendAsync("CSS.forcePseudoState", new Dictionary<string, object> { ["nodeId"] = nodeId, ["forcedPseudoClasses"] = Array.Empty<string>() });
        }

        // The pointer's own walk: what is lit, and where focus is, with the pointer on each row and after it left.
        if (!view.Phone)
        {
            var pointer = new JsonArray();
            example["pointer"] = pointer;
            foreach (var row in rows)
            {
                await MoveTo(row, 3);
                await page.WaitForTimeoutAsync(60);
                pointer.Add(await page.Run(Lit, rowsArg));
            }

            // The keyboard cursor, a row at a time from the top.
            await page.Mouse.MoveAsync(3, 3, new MouseMoveOptions { Steps = 3 });
            await page.WaitForTimeoutAsync(60);
            pointer.Add(await page.Run(Lit, rowsArg));
        }

        var walk = new JsonArray();
        example["walk"] = walk;
        for (var step = 0; step < rows.Count; step++)
        {
            // The same key on both sides: the kit's cursor is the runtime's (data-rask-menu-cursor), in the browser, as
            // Flux's is its script's. A navigation menu has no cursor on either: its links are reached with Tab.
            await page.Keyboard.PressAsync("ArrowDown");

            await page.WaitForTimeoutAsync(60);
            var at = Js.Str(await page.Run("() => document.activeElement?.getAttribute('data-pm-n') ?? null"));
            walk.Add(at is null ? null : JsonValue.Create(rows.IndexOf((int)Js.Parse(at))));
            if (at is not null)
            {
                states.Add(new JsonObject { ["row"] = Js.Parse(at), ["state"] = "cursor", ["changed"] = await page.Run(Diff, styles) });
            }
        }

        // The pointer and the keys together, on a menu that takes focus and has rows to spare: the pointer lights the
        // second row and the first arrow lands ON it; the next goes on; the pointer lights the first row while focus
        // stays, and the arrow after that counts from the lit row; the pointer leaves and the keyboard's row stays lit.
        if (!view.Phone && kind == "dropdown" && Js.Str(example["focus"]) == "menu" && rows.Count >= 3)
        {
            await Reopen();
            var steps = new Func<Task>[]
            {
                () => MoveTo(rows[1], 3), () => page.Keyboard.PressAsync("ArrowDown"), () => page.Keyboard.PressAsync("ArrowDown"), () => MoveTo(rows[0], 3),
                () => page.Keyboard.PressAsync("ArrowDown"), () => page.Mouse.MoveAsync(3, 3, new MouseMoveOptions { Steps = 3 }),
            };
            var together = new JsonArray();
            example["together"] = together;
            foreach (var step in steps)
            {
                await step();
                await page.WaitForTimeoutAsync(80);
                together.Add(await page.Run(Where, rowsArg));
            }

            await Reopen();
        }

        // On a phone, every row tapped in a menu opened afresh.
        if (view.Phone)
        {
            var taps = new JsonArray();
            example["taps"] = taps;
            foreach (var row in rows)
            {
                await Reopen();
                var box = await page.Locator($"[data-pm-n=\"{row}\"]").BoundingBoxAsync();
                if (box is null)
                {
                    taps.Add("not on screen");
                    continue;
                }

                await Screen.Press(page, box.X + (box.Width / 2), box.Y + (box.Height / 2), true);
                await page.WaitForTimeoutAsync(300);
                taps.Add(await page.Run(Tapped, rowsArg));
            }

            await Reopen();
        }

        // The first submenu, opened the way a pointer opens it; on a phone by a tap on its row, then tapped again.
        if (example["submenu"] is JsonValue submenuValue)
        {
            var submenu = (int)Js.Number(submenuValue);
            var box = (await page.Locator($"[data-pm-n=\"{submenu}\"]").BoundingBoxAsync())!;
            var flyoutArg = new { STYLES = Pressables.Styles, PRESSABLE = Pressables.Pressable, row = submenu };
            if (view.Phone)
            {
                await Screen.Press(page, box.X + (box.Width / 2), box.Y + (box.Height / 2), true);
                await page.WaitForTimeoutAsync(400);
                example["flyout"] = await page.Run(CollectFlyout, flyoutArg);
                await Screen.Press(page, box.X + (box.Width / 2), box.Y + (box.Height / 2), true);
                await page.WaitForTimeoutAsync(400);
                example["again"] = await page.Run(Tapped, rowsArg);
            }
            else
            {
                await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
                await page.WaitForTimeoutAsync(400);
                example["flyout"] = await page.Run(CollectFlyout, flyoutArg);

                // The diagonal: from the middle of the row to the far corner of its flyout, across the rows below.
                var far = (await page.Run("""
                    row => {
                      const flyout = document.querySelector(`[data-pm-n="${row}"]`).parentElement.querySelector('[role=menu]').getBoundingClientRect();
                      return [flyout.x + 8, flyout.y + flyout.height - 6];
                    }
                    """, submenu) as JsonArray ?? []).Select(Js.Number).ToArray();
                double[] from = [box.X + (box.Width / 2), box.Y + (box.Height / 2)];
                var crossingArg = new JsonObject { ["row"] = submenu, ["rows"] = rowsArg?.DeepClone() };
                var diagonal = new JsonArray();
                example["diagonal"] = diagonal;
                for (var step = 1; step <= 8; step++)
                {
                    await page.Mouse.MoveAsync((float)(from[0] + ((far[0] - from[0]) * step / 8)), (float)(from[1] + ((far[1] - from[1]) * step / 8)));
                    await page.WaitForTimeoutAsync(40);
                    diagonal.Add(await page.Run(Crossing, crossingArg));
                }

                // In a menu opened afresh, onto the row, then straight out of the menu, away from the flyout, at a hand's
                // pace (a move every 130 ms: Flux hears one per 100 ms or so): the flyout closes on the first move heard
                // outside. Afresh, because the way back from the flyout's corner crosses rows Flux closes it for.
                await Reopen();
                await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), new MouseMoveOptions { Steps = 4 });
                await page.WaitForTimeoutAsync(400);
                var menu = (await page.Locator("[data-pm=\"panel\"]").BoundingBoxAsync())!;
                var away = new JsonArray();
                example["away"] = away;
                for (var x = (double)box.X + (box.Width / 2.0) - 30; x > (double)menu.X - 70; x -= 30)
                {
                    await page.Mouse.MoveAsync((float)x, box.Y + (box.Height / 2));
                    await page.WaitForTimeoutAsync(130);
                    var crossed = (JsonObject)(await page.Run(Crossing, crossingArg))!;
                    foreach (var (key, value) in (JsonObject)(await page.Run(Lit, rowsArg))!)
                    {
                        crossed[key] = value?.DeepClone();
                    }

                    away.Add(crossed);
                }
            }
        }

        // Leaving: Escape, then a click outside.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(150);
        example["escape"] = await page.Run(Closed);
        await Open();
        if (!view.Phone)
        {
            await page.Mouse.MoveAsync(3, 3);
        }

        await Screen.Press(page, 3, 3, view.Phone);
        await page.WaitForTimeoutAsync(150);
        example["outside"] = await page.Run(Closed);
        await page.Run(Unmark);
        return example;
    }

    // The page behind an open menu, in a browser that shows its scrollbars: where it is scrolled to and where the first
    // example sits, before, while and after its menu is open, and what <html> is given meanwhile.
    private static async Task<JsonObject> MeasureBehind(string url, string prefix)
    {
        var shown = await Browsers.Chromium(new BrowserTypeLaunchOptions { IgnoreDefaultArgs = ["--hide-scrollbars"] });
        var sizes = new JsonObject();
        foreach (var (size, width, height) in Viewports)
        {
            var page = await shown.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = width, Height = height } });
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            if (prefix == "data-ui-")
            {
                await Runtime.With(page, "rask-hooks.ts");
                await page.Run("() => { document.body.style.paddingBottom = '100vh'; }");
            }

            var kind = Js.Str(await page.Run("""
                prefix => {
                  const host = document.querySelector(`[data-preview-wrapper] :is([${prefix}dropdown], [${prefix}context])`);
                  host.closest('[data-preview-wrapper]').setAttribute('data-pm', 'behind');
                  host.firstElementChild.setAttribute('data-pm', 'trigger');
                  host.firstElementChild.scrollIntoView({ block: 'center' });
                  return host.hasAttribute(`${prefix}context`) ? 'context' : 'dropdown';
                }
                """, prefix));
            await page.WaitForTimeoutAsync(200);
            async Task<JsonObject> Place() => (JsonObject)(await page.Run("""
                () => {
                  const box = document.querySelector('[data-pm="behind"]').getBoundingClientRect();
                  const html = getComputedStyle(document.documentElement);
                  return { at: [window.scrollY, box.x, box.width], scrollbar: window.innerWidth > document.documentElement.clientWidth,
                    lock: `${html.overflowY} | ${html.pointerEvents} | ${html.scrollbarGutter}` };
                }
                """))!;
            var before = await Place();
            var box = (await page.Locator("[data-pm=\"trigger\"]").BoundingBoxAsync())!;
            double[] point = [Js.Round(box.X + (box.Width / 2)), Js.Round(box.Y + (box.Height / 2))];
            await page.Mouse.ClickAsync((float)point[0], (float)point[1], new MouseClickOptions { Button = kind == "context" ? MouseButton.Right : MouseButton.Left });
            if (kind == "context" && prefix == "data-ui-")
            {
                await page.Run(ShowAtPointer, point);
            }

            await page.WaitForTimeoutAsync(300);
            var during = await Place();
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(300);
            var after = await Place();
            var origin = (before["at"] as JsonArray ?? []).Select(Js.Number).ToArray();
            string Moved(JsonObject step) => string.Join(',', (step["at"] as JsonArray ?? []).Select((v, i) => Js.Num(Js.Round((Js.Number(v) - origin[i]) * 100) / 100)));
            sizes[size] = new JsonObject
            {
                ["scrollbar"] = before["scrollbar"]?.DeepClone(),
                ["lock"] = during["lock"]?.DeepClone(),
                ["moved"] = $"{Moved(during)} then {Moved(after)}",
                ["after"] = after["lock"]?.DeepClone(),
            };
            await page.CloseAsync();
        }

        await shown.CloseAsync();
        return sizes;
    }

    // ---- comparing ------------------------------------------------------------------------------------

    private static string Stringified(JsonNode? node, bool present) => present ? Js.Stringify(node) : "undefined";

    private static List<string> CompareExample(JsonObject theirs, JsonObject mine)
    {
        var diffs = new List<string>();
        void Say(string what, string a, string b)
        {
            if (a != b)
            {
                diffs.Add($"{what}: {a} vs {b}");
            }
        }

        void SayKey(string what, string key) => Say(what, JsString.Json(theirs, key), JsString.Json(mine, key));
        SayKey("kind", "kind");
        if (!Js.Truthy(theirs["kind"]) || !Js.Truthy(mine["kind"]))
        {
            return diffs;
        }

        SayKey("opened", "opened");
        if (!Js.Truthy(theirs["opened"]) || !Js.Truthy(mine["opened"]))
        {
            return diffs;
        }

        var context = Js.Str(theirs["kind"]) == "context";
        // ACCEPTED, and deliberate: Flux's context menu opens without taking focus, so its arrow keys do nothing until a
        // row is clicked. Rask.Ui's takes focus as a dropdown's does — a menu a keyboard cannot use is not one a keyboard
        // user was given.
        if (!context)
        {
            SayKey("focus after opening", "focus");
            SayKey("the cursor walks", "walk");
        }

        SayKey("the page behind", "lock");
        // A navigation menu's links are lit by :hover on both sides, and have no cursor. Where focus is under the pointer
        // is compared with the menu's own focus: not on a context menu (above).
        string Lights(JsonObject example) => example["pointer"] is JsonArray pointer
            ? Js.Stringify(new JsonArray([.. pointer.Select(step => context ? step?["lit"]?.DeepClone() : step?.DeepClone())]))
            : "undefined";
        Say("the pointer lights", Lights(theirs), Lights(mine));
        SayKey("the diagonal to the flyout", "diagonal");
        // Where focus is under the pointer: not on a context menu (above).
        string Away(JsonObject example)
        {
            if (example["away"] is not JsonArray away)
            {
                return "undefined";
            }

            return Js.Stringify(new JsonArray([.. away.Select(step =>
            {
                var copy = step?.DeepClone();
                if (context && copy is JsonObject obj)
                {
                    obj.Remove("focus");
                }

                return copy;
            })]));
        }

        Say("the pointer leaving the submenu's row out of the menu", Away(theirs), Away(mine));
        SayKey("the pointer and the keys together", "together");
        SayKey("a tap on each row", "taps");
        SayKey("the submenu's row tapped again", "again");
        var (rowsA, rowsB) = (Ints(theirs["rows"]), Ints(mine["rows"]));
        Say("rows", Js.Num(rowsA.Count), Js.Num(rowsB.Count));
        SayKey("Escape", "escape");
        // Only that it closed. Flux then puts focus back on the trigger from script; the browser's own light dismiss
        // leaves it on the page, and Rask's runtime hands it back — which a static parity page does not load.
        Say("a click outside closes it", JsString.Json(theirs["outside"] as JsonObject, "closed"), JsString.Json(mine["outside"] as JsonObject, "closed"));

        var nodesA = Nodes(theirs["nodes"]);
        var nodesB = Nodes(mine["nodes"]);
        var (a, b) = (nodesA[0], nodesB[0]);
        // Where the popup sits against its trigger, which a tree compared from its own root would not say.
        if (Math.Abs(Box(a)[0] - Box(b)[0]) > 0.6 || Math.Abs(Box(a)[1] - Box(b)[1]) > 0.6)
        {
            diffs.Add($"placement: {Compare.Fix(Box(a)[0])},{Compare.Fix(Box(a)[1])} vs {Compare.Fix(Box(b)[0])},{Compare.Fix(Box(b)[1])} from the trigger");
        }

        var pairs = new List<(string Id, string Twin, string Where)>();
        CompareTree(nodesA, a, nodesB, b, a, b, "menu", diffs, pairs);

        var statesA = (theirs["states"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var statesB = (mine["states"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        foreach (var state in new[] { "hover", "press", "cursor" })
        {
            for (var i = 0; i < rowsA.Count; i++)
            {
                JsonObject? Changed(List<JsonObject> states, int? row) => row is null ? null
                    : states.FirstOrDefault(s => (int)Js.Number(s["row"]) == row && Js.Str(s["state"]) == state)?["changed"] as JsonObject;
                var x = Changed(statesA, rowsA[i]);
                var y = Changed(statesB, i < rowsB.Count ? rowsB[i] : null);
                if (x is null || y is null)
                {
                    // The cursor never lands on a context menu's rows in Flux: see the note on focus above.
                    if ((x is null) != (y is null) && !(state == "cursor" && context))
                    {
                        diffs.Add($"row[{i}]:{state}: {(x is not null ? "only Flux reaches it" : "only Rask reaches it")}");
                    }

                    continue;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (id, twin, where) in pairs)
                {
                    var changed = Visible(x[id] as JsonObject ?? [], Style(nodesA.First(n => Id(n) == id)));
                    var other = Visible(y[twin] as JsonObject ?? [], Style(nodesB.First(n => Id(n) == twin)));
                    seen.Add(twin);
                    foreach (var key in changed.Select(p => p.Key).Concat(other.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
                    {
                        var (cv, ov) = (Js.Str(changed[key]), Js.Str(other[key]));
                        if (!Same(cv, ov))
                        {
                            diffs.Add($"row[{i}]:{state} {where} {key}: {cv ?? "(unchanged)"} vs {ov ?? "(unchanged)"}");
                        }
                    }
                }

                foreach (var (id, value) in y.Where(p => !seen.Contains(p.Key)))
                {
                    diffs.Add($"row[{i}]:{state}: Rask also changes node {id}: {Js.Stringify(value)}");
                }
            }
        }

        var (flyoutA, flyoutB) = (theirs["flyout"] as JsonArray, mine["flyout"] as JsonArray);
        if ((flyoutA is null) != (flyoutB is null))
        {
            diffs.Add($"flyout: {(flyoutA is not null ? "only in Flux" : "only in Rask")}");
        }
        else if (flyoutA is not null)
        {
            var (fa, fb) = (Nodes(flyoutA), Nodes(flyoutB));
            if (Math.Abs(Box(fa[0])[0] - Box(fb[0])[0]) > 0.6 || Math.Abs(Box(fa[0])[1] - Box(fb[0])[1]) > 0.6)
            {
                diffs.Add($"flyout placement: {Compare.Fix(Box(fa[0])[0])},{Compare.Fix(Box(fa[0])[1])} vs {Compare.Fix(Box(fb[0])[0])},{Compare.Fix(Box(fb[0])[1])} from its row");
            }

            CompareTree(fa, fa[0], fb, fb[0], fa[0], fb[0], "flyout", diffs, []);
        }

        return diffs;
    }

    private static List<JsonObject> Nodes(JsonNode? nodes) => (nodes as JsonArray ?? []).OfType<JsonObject>().ToList();

    private static string Id(JsonObject node) => JsString.Of(node, "id");

    private static string? Parent(JsonObject node) => node["parent"] is null ? null : JsString.Of(node, "parent");

    private static string Tag(JsonObject node) => Js.Str(node["tag"]) ?? "";

    private static double[] Box(JsonObject node) => (node["box"] as JsonArray ?? []).Select(Js.Number).ToArray();

    private static JsonObject Style(JsonObject node) => node["style"] as JsonObject ?? [];

    private static JsonObject Attrs(JsonObject node) => node["attrs"] as JsonObject ?? [];

    private static void CompareTree(List<JsonObject> theirs, JsonObject a, List<JsonObject> mine, JsonObject b, JsonObject rootA, JsonObject rootB, string where,
        List<string> diffs, List<(string Id, string Twin, string Where)> pairs)
    {
        pairs.RemoveAll(pair => pair.Id == Id(a));
        pairs.Add((Id(a), Id(b), where));
        if (Native.GetValueOrDefault(Tag(a), Tag(a)) != Tag(b))
        {
            diffs.Add($"{where}: tag <{Tag(a)}> vs <{Tag(b)}>");
        }

        if (JsString.Of(a, "text") != JsString.Of(b, "text"))
        {
            diffs.Add($"{where}: text \"{JsString.Of(a, "text")}\" vs \"{JsString.Of(b, "text")}\"");
        }

        foreach (var name in new[] { "role", "aria-checked" })
        {
            if (JsString.Json(Attrs(a), name) != JsString.Json(Attrs(b), name))
            {
                diffs.Add($"{where}: {name} {JsString.Of(Attrs(a), name)} vs {JsString.Of(Attrs(b), name)}");
            }
        }

        var (ba, bb) = (Box(a), Box(b));
        if (Math.Abs(ba[2] - bb[2]) > 0.6 || Math.Abs(ba[3] - bb[3]) > 0.6)
        {
            diffs.Add($"{where}: size {Js.Num(ba[2])}x{Js.Num(ba[3])} vs {Js.Num(bb[2])}x{Js.Num(bb[3])}");
        }

        var displayed = Js.Str(Style(a)["display"]) != "none" || Js.Str(Style(b)["display"]) != "none";
        static bool Shown(double[] box) => box[2] > 0 || box[3] > 0;
        if (!ReferenceEquals(a, rootA) && displayed && (Shown(ba) || Shown(bb)))
        {
            var (ra, rb) = (Box(rootA), Box(rootB));
            if (Math.Abs(ba[0] - ra[0] - (bb[0] - rb[0])) > 0.6 || Math.Abs(ba[1] - ra[1] - (bb[1] - rb[1])) > 0.6)
            {
                diffs.Add($"{where}: offset {Compare.Fix(ba[0] - ra[0])},{Compare.Fix(ba[1] - ra[1])} vs {Compare.Fix(bb[0] - rb[0])},{Compare.Fix(bb[1] - rb[1])}");
            }
        }

        var departs = DepartedFrom(mine, b);
        var pressable = Js.Truthy(b["pressable"]);
        CompareStyles(Style(a), Style(b), where, diffs, ReferenceEquals(a, rootA), pressable, departs);
        foreach (var pseudo in new[] { "::before", "::after" })
        {
            var (pa, pb) = (a[pseudo] as JsonObject, b[pseudo] as JsonObject);
            if ((pa is null) != (pb is null))
            {
                diffs.Add($"{where}{pseudo}: {(pa is not null ? "only in Flux" : "only in Rask")}");
            }

            // A pseudo-element inherits what its node departs in (a row's `user-select`).
            else if (pa is not null)
            {
                CompareStyles(pa, pb!, $"{where}{pseudo}", diffs, false, pressable, departs);
            }
        }

        var ca = theirs.Where(c => Parent(c) == Id(a)).ToList();
        var cb = mine.Where(c => Parent(c) == Id(b)).ToList();
        if (ca.Count != cb.Count)
        {
            diffs.Add($"{where}: children <{string.Join(' ', ca.Select(Tag))}> vs <{string.Join(' ', cb.Select(Tag))}>");
            return;
        }

        for (var i = 0; i < ca.Count; i++)
        {
            CompareTree(theirs, ca[i], mine, cb[i], rootA, rootB, $"{where} > {Tag(ca[i])}[{i}]", diffs, pairs);
        }
    }

    // The owner-approved NATIVE FEEL departure (2026-11-02, flux-component SKILL.md, ui.css), as parity lets it go, in a
    // menu: a row — an item, or a navigation menu's link — selects no text, waits for no second tap and opens no
    // long-press callout, and so does what is inside it (`user-select` inherits in Blink); a menu, the popup or a flyout,
    // keeps its own scroll. Asked of the kit's node and the tree above it; nothing of Flux's is.
    private static List<string> DepartedFrom(List<JsonObject> nodes, JsonObject node)
    {
        var departs = Js.Str(Attrs(node)["role"]) == "menu" || Attrs(node).ContainsKey("popover") ? new List<string> { "overscrollBehaviorY" } : [];
        for (var n = node; n is not null; n = nodes.FirstOrDefault(p => Parent(n) is { } parent && Id(p) == parent))
        {
            if ((Js.Str(Attrs(n)["role"])?.StartsWith("menuitem", StringComparison.Ordinal) ?? false) || Tag(n) == "a")
            {
                return [.. departs, .. RowFeel];
            }
        }

        return departs;
    }

    // `pressable`: the kit's node shows the hand by the owner-approved departure (Pressables.CursorDeparted).
    // `departs`: the keys DepartedFrom lets go on this node.
    private static void CompareStyles(JsonObject x, JsonObject y, string where, List<string> diffs, bool placed, bool pressable, List<string> departs)
    {
        foreach (var key in Pressables.Styles)
        {
            var (xv, yv) = (Js.Str(x[key]), Js.Str(y[key]));
            if (Ignored.Contains(key) || (placed && Placed.Contains(key)) || departs.Contains(key) || Same(xv, yv))
            {
                continue;
            }

            if (key == "cursor" && Pressables.CursorDeparted(xv, yv, pressable ? "true" : "false"))
            {
                continue;
            }

            var width = BorderColor().Match(key);
            if (width.Success && Js.Str(x[$"border{width.Groups[1].Value}Width"]) == "0px" && Js.Str(y[$"border{width.Groups[1].Value}Width"]) == "0px")
            {
                continue;
            }

            // Nor the width and colour of an outline neither side draws: a focused element computes the browser's
            // focus-ring values for them even with the ring turned off.
            if (key is "outlineColor" or "outlineWidth" && Js.Str(x["outlineStyle"]) == "none" && Js.Str(y["outlineStyle"]) == "none")
            {
                continue;
            }

            diffs.Add($"{where}: {key}: {xv ?? "undefined"} vs {yv ?? "undefined"}");
        }
    }

    // A state's changes, without the ones nobody can see: the box (compared as a box), and the colour and width of an
    // outline or a border that is not drawn — both follow the text colour, and a row's ink changes.
    private static JsonObject Visible(JsonObject changed, JsonObject node)
    {
        var shown = new JsonObject();
        foreach (var (key, value) in changed)
        {
            if (Ignored.Contains(key))
            {
                continue;
            }

            if (key is "outlineColor" or "outlineWidth" && (Js.Str(changed["outlineStyle"]) ?? Js.Str(node["outlineStyle"])) == "none")
            {
                continue;
            }

            var side = BorderColor().Match(key);
            if (side.Success && Js.Str(node[$"border{side.Groups[1].Value}Width"]) == "0px")
            {
                continue;
            }

            shown[key] = value?.DeepClone();
        }

        return shown;
    }

    // `none` is a hue that has no say (a gray's): a minifier writes it for the 0 the source had.
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

        string Round(string v) => NoneHue().Replace(Decimal().Replace(v, m => Js.Num(Js.Round(Js.Parse(m.Value) * 1000) / 1000)), " 0)");
        return Round(x) == Round(y);
    }

    [GeneratedRegex("^border(Top|Right|Bottom|Left)Color$")]
    private static partial Regex BorderColor();

    [GeneratedRegex("-?[0-9]*\\.[0-9]+(e-?[0-9]+)?")]
    private static partial Regex Decimal();

    [GeneratedRegex(" none\\)")]
    private static partial Regex NoneHue();
}
