using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>
///     Where an open panel LANDS when there is no room for it where it asked to be.
/// </summary>
/// <remarks>
///     A docs page shows every example in the middle of a wide column, where a panel always fits. What an application has
///     is a filter by the right edge, a user menu at the bottom of a sidebar, a select in the last row of a form, all of
///     it again on a phone. So each family's open-state check hands this its examples, and every one is opened with its
///     trigger HELD at the edges of a phone and of a desktop — the host made <c>position: fixed</c> at that place, on
///     Flux's live page and on the Rask parity page alike — and with more in the panel than there is room for. Where the
///     panel then is in the viewport, and the height it was held to, must agree: Flux flips the side, flips the
///     alignment, slides the panel back 5 px inside the edge and caps its height (the kit: rask-place.ts).
///     <para>
///         Flux's side is measured once and kept beside its other measurements (<c>edges.json</c>; <c>--refresh</c>
///         measures again, and so does any change to what is asked). Each step waits on STATE: the panel in the same box
///         for three frames.
///     </para>
/// </remarks>
public static class Placement
{
    // What `hold` and `read` do to a page. Raised whenever that changes: Flux's kept measurements are then stale.
    private const int Harness = 3;

    /// <summary>A phone and a desktop.</summary>
    public static readonly int[][] Views = [[390, 844], [1920, 1080]];

    /// <summary>
    ///     Where the host is held — its left and top, a negative number being its far edge's distance from the viewport's
    ///     far edge, 'mid' the middle — and what is added to the panel (<c>fill</c>: width, height). An <c>inside</c> edge
    ///     is moved from the middle until the panel that opened there would end this far inside an edge: within the 5 px a
    ///     panel keeps from it, and just past it.
    /// </summary>
    public static JsonArray Edges => (JsonArray)JsonNode.Parse("""
        [
          { "name": "in the middle", "at": ["mid", "mid"] },
          { "name": "at the left edge", "at": [5, "mid"] },
          { "name": "at the right edge", "at": [-5, "mid"] },
          { "name": "near the right edge", "at": [-40, "mid"] },
          { "name": "at the top", "at": ["mid", 70] },
          { "name": "at the bottom", "at": ["mid", -5] },
          { "name": "in the bottom right corner", "at": [-5, -5] },
          { "name": "in the bottom left corner", "at": [5, -5] },
          { "name": "in the top right corner", "at": [-5, 70] },
          { "name": "taller than the room under it", "at": ["mid", "mid"], "fill": [1, 1500] },
          { "name": "taller than the room, low in the viewport", "at": ["mid", -120], "fill": [1, 1500] },
          { "name": "taller than the room, high in the viewport", "at": ["mid", 120], "fill": [1, 1500] },
          { "name": "wider than the viewport", "at": ["mid", "mid"], "fill": [2500, 1] },
          { "name": "its bottom edge 3 px inside the viewport's", "inside": ["bottom", 3] },
          { "name": "its bottom edge 6 px past the viewport's", "inside": ["bottom", -6] },
          { "name": "its top edge 3 px inside the viewport's", "inside": ["top", 3] },
          { "name": "its right edge 3 px inside the viewport's", "inside": ["right", 3] },
          { "name": "its right edge 6 px past the viewport's", "inside": ["right", -6] },
          { "name": "its left edge 3 px inside the viewport's", "inside": ["left", 3] }
        ]
        """)!;

    /// <summary>How a page opens a held host that cannot open by the gesture itself: (page, point, how).</summary>
    public delegate Task Opener(IPage page, double[] point, string how);

    /// <summary>
    ///     One page of a family: <see cref="Host" /> a selector matched in document order on that page, <see cref="Panel" />
    ///     and <see cref="Trigger" /> selectors inside the host (the trigger is its first child without one, and the host
    ///     itself when the selector matches it), <see cref="Open" /> for a page that cannot open by the gesture itself,
    ///     <see cref="Prepare" /> run once the page has loaded.
    /// </summary>
    public sealed record Side(string Url, string Host, string? Panel, string? Trigger, Opener? Open = null, Func<IPage, Task>? Prepare = null);

    /// <summary>A host: its place among the page's matches, how it opens (click, hover, context) and a name for the report.</summary>
    public sealed record Host(int Index, string? Open = null, string? Name = null);

    /// <summary>
    ///     A family of controls: <see cref="Scrollbars" /> <c>"shown"</c> when the browser was launched showing them, which
    ///     Flux's kept side is keyed on.
    /// </summary>
    public sealed record Family(string Name, Side Flux, Side Rask, IReadOnlyList<Host> Hosts)
    {
        /// <summary>The viewports, <see cref="Views" /> unless given.</summary>
        public int[][]? Views { get; init; }

        /// <summary>The places, <see cref="Edges" /> unless given.</summary>
        public JsonArray? Edges { get; init; }

        /// <summary>The width the host is held at; its own (max-content) unless given.</summary>
        public int? Width { get; init; }

        /// <summary>Measure Flux's side again.</summary>
        public bool Refresh { get; init; }

        /// <summary>Print every place, not only those that differ.</summary>
        public bool All { get; init; }

        /// <summary>"shown" when the browser shows its scrollbars.</summary>
        public string? Scrollbars { get; init; }
    }

    /// <summary>Opens every host of <paramref name="family" /> at every edge on both pages and prints one line per host and viewport; answers how many differ.</summary>
    public static async Task<int> Landings(IBrowser browser, Family family)
    {
        var views = family.Views ?? Views;
        var edges = family.Edges ?? Edges;
        var hosts = new JsonArray([.. family.Hosts.Select(h =>
        {
            var host = new JsonObject { ["index"] = h.Index };
            if (h.Open is not null)
            {
                host["open"] = h.Open;
            }

            if (h.Name is not null)
            {
                host["name"] = h.Name;
            }

            return (JsonNode?)host;
        })]);
        var asked = new JsonObject
        {
            ["HARNESS"] = Harness,
            ["views"] = Js.ToNode(views),
            ["edges"] = edges.DeepClone(),
            ["hosts"] = hosts.DeepClone(),
        };
        if (family.Width is { } width)
        {
            asked["width"] = width;
        }

        asked["host"] = family.Flux.Host;
        if (family.Flux.Panel is not null)
        {
            asked["panel"] = family.Flux.Panel;
        }

        if (family.Scrollbars is not null)
        {
            asked["scrollbars"] = family.Scrollbars;
        }

        var askedText = Js.Stringify(asked);
        var file = Repo.Parity("flux", family.Name, "edges.json");
        var flux = File.Exists(file) && !family.Refresh ? JsonNode.Parse(await File.ReadAllTextAsync(file)) as JsonObject : null;
        if (flux is null || Js.Str(flux["asked"]) != askedText)
        {
            var (landedFlux, inheritedFlux) = await MeasureSide(browser, family, family.Flux, views, edges, family.Hosts, null);
            flux = new JsonObject { ["asked"] = askedText, ["landed"] = landedFlux, ["inherited"] = inheritedFlux };
            Directory.CreateDirectory(Repo.Parity("flux", family.Name));
            await File.WriteAllTextAsync(file, Js.Stringify(flux));
        }

        var (rask, _) = await MeasureSide(browser, family, family.Rask, views, edges, family.Hosts, flux["inherited"] as JsonObject);
        var failures = 0;
        foreach (var (key, theirs) in (JsonObject)flux["landed"]!)
        {
            var mine = rask[key] as JsonArray ?? [];
            var diffs = new List<string>();
            var landed = (JsonArray)theirs!;
            for (var i = 0; i < landed.Count; i++)
            {
                var difference = Differs(landed[i]!, i < mine.Count ? mine[i] : null);
                if (difference is not null)
                {
                    diffs.Add($"{Js.Str(edges[i]!["name"])}: {difference}");
                }
                else if (family.All)
                {
                    diffs.Add($"{Js.Str(edges[i]!["name"])}: {Say(landed[i])}");
                }
            }

            var differing = diffs.Count(d => d.Contains(" vs ", StringComparison.Ordinal));
            failures += differing > 0 ? 1 : 0;
            Console.WriteLine($"{(differing > 0 ? "FAIL" : "ok  ")} {family.Name} lands as Flux's, {key} — {edges.Count} places{(differing > 0 ? $", {differing} differ" : "")}");
            foreach (var d in diffs)
            {
                Console.WriteLine($"       {d}");
            }
        }

        return failures;
    }

    private static string R(JsonNode? value) => Js.Num(Js.Round2(Js.Number(value)));

    private static string Say(JsonNode? landed)
    {
        if (landed is null || !Js.Truthy(landed["open"]))
        {
            return "not open";
        }

        var panel = (JsonArray)landed["panel"]!;
        return $"x={R(panel[0])}..{R(panel[2])} y={R(panel[1])}..{R(panel[3])}{(landed["cap"] is null ? "" : $" max-height {R(landed["cap"])}")}";
    }

    // A pixel either way: the two pages lay the same panel out to within a rounding.
    private static string? Differs(JsonNode theirs, JsonNode? mine)
    {
        if (mine is null)
        {
            return $"{Say(theirs)} vs nothing measured";
        }

        static bool Near(JsonNode? a, JsonNode? b) => a is null || b is null ? a is null && b is null : Math.Abs(Js.Number(a) - Js.Number(b)) <= 1;
        static bool All(JsonNode? x, JsonNode? y) => (x as JsonArray ?? []).Select((v, i) => Near(v, (y as JsonArray)?[i])).All(ok => ok);
        var open = Js.Truthy(theirs["open"]);
        var same = open == Js.Truthy(mine["open"]) && (!open || (All(theirs["panel"], mine["panel"]) && Near(theirs["cap"], mine["cap"])));
        if (!same)
        {
            return $"{Say(theirs)} vs {Say(mine)}";
        }

        // The harness's own check: a trigger that is not in the same place was not asked the same question.
        static string Rounded(JsonNode? t) => string.Join(',', (t as JsonArray ?? []).Select(R));
        return All(theirs["trigger"], mine["trigger"]) ? null : $"the triggers are not in the same place: {Rounded(theirs["trigger"])} vs {Rounded(mine["trigger"])}";
    }

    // `inherited`: Flux's side of each host, which makes this the Rask page — given the runtime's hooks, and the
    // text its docs page hands down (font, size, line height), without which a panel is a pixel or two another size.
    private static async Task<(JsonObject Landed, JsonObject Inherited)> MeasureSide(IBrowser browser, Family family, Side side, int[][] views, JsonArray edges, IReadOnlyList<Host> hosts, JsonObject? inherited)
    {
        var hooks = inherited is not null;
        var landed = new JsonObject();
        var found = new JsonObject();
        foreach (var view in views)
        {
            var (width, height) = (view[0], view[1]);
            foreach (var host in hosts)
            {
                var context = await browser.NewContextAsync(Screen.Of(width, height));
                // The day Measure pins: a calendar that opens on today is another height in a month of six weeks.
                await context.Clock.SetFixedTimeAsync("2026-01-15T12:00:00Z");
                var page = await context.NewPageAsync();
                await page.GotoAsync(side.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
                await page.Run("() => document.fonts.ready.then(() => undefined)");
                // A parity page is static markup: what Flux does in script the kit asks the runtime's hooks for.
                if (hooks && !Js.Truthy(await page.Run("() => Boolean(globalThis.__raskHookSeam)")))
                {
                    await Runtime.With(page, "rask-hooks.ts");
                }

                // The room a panel is laid out in is the same on both pages. Flux's lock puts `scrollbar-gutter: stable` on
                // <html> under an open panel, and in a browser that hides its scrollbars (every headless one) that takes 15 px
                // off the right of what a panel hangs in: one opened by the right edge is squeezed there (to its min-width),
                // and lands where that width puts it. The kit's lock asks for the gutter only where a scrollbar takes room
                // (rask-lock.ts, held by parity-menu's page behind), so here the Rask page is given Flux's declaration
                // under the same lock: what is compared is where the placer puts a panel, not whether a gutter was kept.
                if (hooks)
                {
                    await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = "html[data-rask-locked]{scrollbar-gutter:stable}" });
                }

                if (side.Prepare is not null)
                {
                    await side.Prepare(page);
                }

                var key = $"{width}x{height} #{host.Index}{(host.Name is not null ? $" {host.Name}" : "")}";
                var places = new JsonArray();
                landed[key] = places;
                var from = inherited?[key]?.DeepClone();
                found[key] = await page.Run("""
                    ({ host, index, from }) => {
                      const el = [...document.querySelectorAll(host)][index];
                      const names = ['fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];
                      if (!el) return null;
                      if (from) Object.assign(el.style, from);
                      const style = getComputedStyle(el);
                      return Object.fromEntries(names.map(name => [name, style[name]]));
                    }
                    """, new JsonObject { ["host"] = side.Host, ["index"] = host.Index, ["from"] = from });
                foreach (var edge in edges)
                {
                    places.Add(await One(page, family, side, host, edge!, places.Count > 0 ? places[0] : null));
                }

                await context.CloseAsync();
            }
        }

        return (landed, found);
    }

    // `middle`: where this host's panel landed in the first place asked for, which an `inside` edge moves from.
    private static async Task<JsonNode> One(IPage page, Family family, Side side, Host host, JsonNode edge, JsonNode? middle)
    {
        var held = await page.Run(Hold, new JsonObject
        {
            ["side"] = new JsonObject { ["host"] = side.Host, ["panel"] = side.Panel, ["trigger"] = side.Trigger },
            ["index"] = host.Index,
            ["edge"] = edge.DeepClone(),
            ["width"] = family.Width,
            ["middle"] = middle?.DeepClone(),
        }) as JsonArray;
        if (held is null)
        {
            return JsonNode.Parse("""{"open":false,"panel":[0,0,0,0],"trigger":[0,0,0,0],"cap":null}""")!;
        }

        double[] point = [Js.Number(held[0]) + Js.Number(held[2]) / 2, Js.Number(held[1]) + Js.Number(held[3]) / 2];
        var how = host.Open ?? "click";
        await page.Mouse.MoveAsync(2, 2);
        if (side.Open is not null)
        {
            await side.Open(page, point, how);
        }
        else if (how == "hover")
        {
            await page.Mouse.MoveAsync((float)point[0], (float)point[1]);
        }
        else if (how == "context")
        {
            await page.Mouse.ClickAsync((float)point[0], (float)point[1], new MouseClickOptions { Button = MouseButton.Right });
        }
        else
        {
            await page.Mouse.ClickAsync((float)point[0], (float)point[1]);
        }

        var result = (await page.Run(Read))!;
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.MoveAsync(2, 2);
        await page.Run(Shut);
        return result;
    }

    // Runs in the page. Holds one host at a place in the viewport; answers its trigger's box.
    private const string Hold = """
        ({ side, index, edge, width, middle }) => {
          const el = [...document.querySelectorAll(side.host)][index];
          if (!el) return null;
          const panel = side.panel ? el.querySelector(side.panel) : null;
          const trigger = !side.trigger ? el.firstElementChild : el.matches(side.trigger) ? el : el.querySelector(side.trigger);
          if (!panel || !trigger) return null;
          window.__placed = { panel, trigger };
          // Property by property: the host's own style attribute may be what names it an anchor.
          Object.assign(el.style, { position: 'fixed', margin: '0', zIndex: '2147483647', width: width ? `${width}px` : 'max-content', left: '0', top: '0' });
          panel.querySelector('[data-placement-fill]')?.remove();
          if (edge.fill) {
            // Into what scrolls: the list inside a panel that holds a search field over it, or the panel.
            const into = panel.querySelector('[role=listbox]') ?? panel;
            const fill = document.createElement('div');
            fill.setAttribute('data-placement-fill', '');
            fill.style.cssText = `width:${edge.fill[0]}px;height:${edge.fill[1]}px;flex:none`;
            into.append(fill);
          }

          const box = el.getBoundingClientRect();
          const room = [document.documentElement.clientWidth, document.documentElement.clientHeight];
          const place = (at, i) => (at === 'mid' ? Math.round((room[i] - [box.width, box.height][i]) / 2) : at < 0 ? room[i] + at - [box.width, box.height][i] : at);
          let [left, top] = (edge.at ?? ['mid', 'mid']).map(place);
          if (edge.inside && middle?.open) {
            const [to, by] = edge.inside;
            const [x, y, right, bottom] = middle.panel;
            if (to === 'bottom') top += room[1] - by - bottom;
            if (to === 'top') top += by - y;
            if (to === 'right') left += room[0] - by - right;
            if (to === 'left') left += by - x;
          }

          el.style.left = `${left}px`;
          el.style.top = `${top}px`;
          const at = trigger.getBoundingClientRect();
          return [at.x, at.y, at.width, at.height];
        }
        """;

    // Runs in the page. The panel once it has been in one box for three frames, or after two seconds of not being.
    private const string Read = """
        () => {
          const { panel, trigger } = window.__placed;
          const shown = () => {
            try {
              return panel.matches(':popover-open') || (panel.tagName === 'DIALOG' && panel.open);
            } catch {
              return panel.getBoundingClientRect().width > 0;
            }
          };

          return new Promise(done => {
            let last = '';
            let same = 0;
            let frames = 0;
            const look = () => {
              const box = panel.getBoundingClientRect();
              const now = [shown(), box.x, box.y, box.width, box.height].join();
              same = now === last ? same + 1 : 0;
              last = now;
              if ((same >= 3 && frames >= 6 && shown()) || ++frames > 120) {
                const cap = parseFloat(getComputedStyle(panel).maxHeight);
                const at = trigger.getBoundingClientRect();
                // A max-height the panel does not reach holds it to nothing.
                done({ open: shown(), panel: [box.left, box.top, box.right, box.bottom], trigger: [at.left, at.top, at.right, at.bottom],
                  cap: Number.isNaN(cap) || cap > box.height + 1 ? null : cap });
              } else requestAnimationFrame(look);
            };
            requestAnimationFrame(look);
          });
        }
        """;

    // Runs in the page. Waits for the panel to be closed, and closes one that Escape did not.
    private const string Shut = """
        () => {
          const { panel } = window.__placed;
          const shown = () => {
            try {
              return panel.matches(':popover-open') || (panel.tagName === 'DIALOG' && panel.open);
            } catch {
              return false;
            }
          };

          return new Promise(done => {
            let frames = 0;
            const look = () => {
              if (!shown() && frames >= 3) done();
              else if (++frames > 60) {
                try { panel.hidePopover(); } catch { /* not a popover */ }
                done();
              } else requestAnimationFrame(look);
            };
            requestAnimationFrame(look);
          });
        }
        """;
}
