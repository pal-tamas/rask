using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Chart's GEOMETRY to Flux's, number by number — what parity cannot see.
/// </summary>
/// <remarks>
///     Parity compares boxes and computed styles. A chart is mostly neither: it is path data, line ends, circle centres and
///     translate()s, and two curves can share a bounding box. This compares those attributes for every SVG node of every
///     chart, example by example, in light and dark.
///     <para>
///         Flux's docs page draws its charts from data its server makes up on EVERY request (random values, dates counted
///         back from now), so two loads never show the same chart. <c>--measure</c> therefore pins one load: the document is
///         fetched once, kept in artifacts/flux-parity/flux/chart/document.html, and replayed for both schemes, so light and
///         dark draw the same numbers; each chart's data is written to tests/Rask.Ui.Tests/Flux/Parity/ChartParity.data.json,
///         which ChartParity.cs draws from (data, not code); the measurement lands where parity caches it, so
///         <c>flux parity chart</c> (WITHOUT --refresh, which would roll new numbers) compares styles against the very same
///         load; all of it on a desktop and on a phone (<see cref="Views" />), from that one document, and each drawing's box
///         with it: ChartParity draws every chart at the box Flux gave it there, and writes a page per view.
///     </para>
///     <code>
///     flux parity-chart --measure     # pin a load of Flux's page (rarely)
///     flux parity-chart --remeasure   # measure the pinned load again, no new numbers
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages
///     flux parity chart [--view 390x844]                 # boxes and styles
///     flux parity-chart [--view 390x844] [--all]         # geometry; exit code 1 over the tolerance
///     flux parity-chart --pointer [--view 390x844]       # what follows the pointer, on the same pin
///     </code>
///     <c>--pointer</c> walks a real pointer over every chart of the pinned load and of the Rask page (given the runtime's
///     hooks, <see cref="Flux.Runtime" />) and compares, step by step, what each shows: where the tooltip is and what it
///     says, where the cursor is, what a summary reads, which points and slices are marked active or inactive.
/// </remarks>
[FluxCommand("parity-chart", "a chart's geometry and pointer against a pinned load of Flux's")]
public sealed partial class ParityChart : IFluxCommand
{
    private const string Url = "https://fluxui.dev/components/chart";

    // px, on every number of every geometry attribute but a `transform`'s…
    private const double Tolerance = 0.05;

    // …and on a node's `transform` (a tick's translate()): 0.5 px (owner, 2026-11-04, #1260). Flux lays its axes out
    // around each tick label's getBBox(), and Chromium on Linux puts a label's glyph overhang in that box where on a
    // Mac it is the advance: the same Inter file, features and size on both (measured: "Nov 4" at 12px is 34.078 px
    // long on both, its box 34.191 px wide on Linux, 34.078 px on a Mac), so on the runner Flux's plot ends 0.11 px
    // short of where the kit's does and every tick of it moves by up to that. The kit lays out from the advances and
    // matches the Mac to the 1/1000 px. On Linux the same narrower plot moves the grid lines and the series' paths by
    // as much (0.11–0.25 px measured), so there every number gets that tolerance; on a Mac the rest keep Tolerance.
    private const double TransformTolerance = 0.5;

    // px: a tooltip's box depends on its text's width, which the two pages round apart.
    private const double Near = 1;

    // Runs in the page. Every chart of every example: its data (Flux's element exposes it) and the geometry of each SVG
    // node it drew. A <template> inside an <svg> is Flux's prototype for a node, never drawn: skipped.
    private const string Geometry = """
        function geometry(selector) {
          const ATTRS = ['d', 'points', 'x', 'y', 'width', 'height', 'x1', 'x2', 'y1', 'y2', 'cx', 'cy', 'r', 'rx', 'ry',
            'transform', 'viewBox', 'stroke-width', 'stroke-dasharray', 'dx', 'dy', 'text-anchor', 'dominant-baseline', 'opacity'];
          const headings = [...document.querySelectorAll('h2[id]')];
          return [...document.querySelectorAll('[data-preview-wrapper]')].map(wrapper => ({
            section: wrapper.dataset.section
              ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '',
            charts: [...wrapper.querySelectorAll(selector)].map(chart => ({
              value: chart.value ?? null,
              box: (({ width, height }) => [width, height])([...chart.querySelectorAll('svg')].find(svg => !svg.closest('template')).getBoundingClientRect()),
              nodes: [...chart.querySelectorAll('svg, svg *')]
                .filter(node => !node.closest('template'))
                .map(node => {
                  const style = getComputedStyle(node);
                  return {
                    tag: node.tagName.toLowerCase(),
                    hidden: style.display === 'none',
                    anchor: style.textAnchor,
                    text: node.children.length ? '' : node.textContent.trim(),
                    attrs: Object.fromEntries(ATTRS.filter(name => node.hasAttribute(name)).map(name => [name, node.getAttribute(name)])),
                  };
                }),
            })),
          }));
        }
        """;

    // Runs in the page: what the chart shows now, in terms both pages have.
    private const string Read = """
        function read({ selector, index }) {
          const chart = document.querySelectorAll(`[data-preview-wrapper] ${selector}`)[index];
          const frame = chart.getBoundingClientRect();
          const svg = [...chart.querySelectorAll('svg')].find(s => !s.closest('template'));
          const origin = svg.getBoundingClientRect();
          const tooltip = chart.querySelector(':scope > [data-rask-plot-tooltip]')
            ?? [...chart.children].find(e => e.tagName === 'DIV' && getComputedStyle(e).position === 'absolute' && !e.hasAttribute('data-ui-chart-hover') && !e.querySelector('svg'));
          const tip = tooltip?.getBoundingClientRect();
          const shown = tooltip ? getComputedStyle(tooltip).opacity === '1' : false;
          // Flux's cursor is a path in the drawing; the kit's a box over it.
          let cursor = null;
          const path = [...svg.querySelectorAll('path[stroke-dasharray][opacity]')].find(p => !p.closest('template'));
          const line = chart.querySelector('[data-rask-plot-area] > div');
          if (line) {
            const box = line.getBoundingClientRect();
            cursor = getComputedStyle(line).display === 'none' ? null : [box.left - origin.left, box.top - origin.top, box.right - origin.left, box.bottom - origin.top];
          } else if (path && Number(path.getAttribute('opacity')) > 0) {
            const n = (path.getAttribute('d').match(/-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? []).map(Number);
            cursor = / h/.test(path.getAttribute('d'))
              ? [n[0], n[1], n[0] + n[2], n[1] + n[3]]
              : [n[0] - 0.5, Math.min(n[1], n[3]), n[0] + 0.5, Math.max(n[1], n[3])];
          }

          const mark = node => node.hasAttribute('data-active') ? 'A' : node.hasAttribute('data-inactive') ? 'i' : '.';
          return {
            tooltip: shown ? { at: [tip.left - frame.left, tip.top - frame.top], text: tooltip.innerText.replace(/\s+/g, ' ').trim() } : null,
            cursor,
            summary: [...chart.querySelectorAll('slot')].filter(s => !s.closest('template') && !tooltip?.contains(s)).map(s => s.textContent.trim()).join(' | '),
            points: [...svg.querySelectorAll('circle')].filter(c => !c.closest('template')).map(mark).join(''),
            slices: [...svg.querySelectorAll('g > path[stroke-linejoin]')].filter(p => !p.closest('template') && p.getAttribute('stroke-width') === '3').map(mark).join(''),
          };
        }
        """;

    // Where the pointer goes, as shares of the drawing's box: across the plot, up and down it, and off it.
    private static readonly double[][] Across = [[0.03, 0.5], [0.2, 0.5], [0.35, 0.5], [0.5, 0.5], [0.65, 0.5], [0.8, 0.5], [0.97, 0.5], [0.5, 0.2], [0.5, 0.75], [0.5, 0.97]];
    private static readonly double[][] Round = [[0.6, 0.3], [0.3, 0.7], [0.75, 0.6], [0.5, 0.5], [0.03, 0.03]];

    // The viewports a load is measured in: parity's desktop, and its phone (`--view 390x844`).
    private static readonly int[][] Views = [[1280, 900], [390, 844]];

    private readonly string output = Repo.Parity();
    private readonly string fluxDir = Repo.Parity("flux", "chart");
    private readonly string dataFile = Repo.Path("tests", "Rask.Ui.Tests", "Flux", "Parity", "ChartParity.data.json");
    private int[] view = Views[0];
    private int limit = 8;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        limit = arg.Flag("all") ? int.MaxValue : 8;
        view = arg.Option("view")?.Split('x').Select(int.Parse).ToArray() ?? Views[0];

        var browser = await Browsers.Chromium();
        if (arg.Flag("measure") || arg.Flag("remeasure"))
        {
            await MeasureFlux(browser, arg.Flag("remeasure"));
        }

        var flux = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fluxDir, $"geometry{Sized(view)}.json")))!;
        var raskPage = Path.Combine(output, "rask", $"chart{Sized(view)}.html");
        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux chart parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            await browser.CloseAsync();
            return 1;
        }

        if (arg.Flag("pointer"))
        {
            var failed = await PointerWalk(browser, raskPage);
            await browser.CloseAsync();
            Console.WriteLine(failed > 0 ? $"\nflux chart parity: the pointer is followed differently in {failed} step(s)." : "\nflux chart parity: the pointer is followed as Flux follows it.");
            return failed > 0 ? 1 : 0;
        }

        var rask = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        await Measure.MeasurePage(browser, Repo.FileUrl(raskPage), Path.Combine(output, "rask", $"chart{Sized(view)}"), async (page, scheme) =>
        {
            rask[scheme] = await page.Run(Geometry, "[data-ui-chart]");
        }, view);
        await browser.CloseAsync();

        var failures = 0;
        foreach (var scheme in new[] { "light", "dark" })
        {
            var theirsAll = flux[scheme] as JsonArray ?? [];
            var mineAll = rask[scheme] as JsonArray ?? [];
            for (var index = 0; index < theirsAll.Count; index++)
            {
                var theirs = theirsAll[index]!;
                var mine = index < mineAll.Count ? mineAll[index] : null;
                var label = $"{scheme} #{index} {Js.Str(theirs["section"])}";
                var (diffs, worst) = Compare(theirs, mine);
                failures += diffs.Count > 0 ? 1 : 0;
                Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {label} — max deviation {Fixed(worst, 4)}px{(diffs.Count > 0 ? $", {diffs.Count} difference(s)" : "")}");
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

        Console.WriteLine(failures > 0 ? $"\nflux chart parity: geometry differs in {failures} example(s)." : "\nflux chart parity: geometry matches Flux.");
        return failures > 0 ? 1 : 0;
    }

    // Parity's name for a viewport of its own: nothing on a desktop, `@390x844` beside it.
    private static string Sized(int[] size) => size[0] == Views[0][0] && size[1] == Views[0][1] ? "" : $"@{size[0]}x{size[1]}";

    private static double Allowed(string name) => name == "transform" || OperatingSystem.IsLinux() ? TransformTolerance : Tolerance;

    // `Number.prototype.toFixed`: a zero, negative or not, prints without its sign.
    private static string Fixed(double value, int digits) => double.IsNaN(value) ? "NaN" : (value == 0 ? 0 : value).ToString($"F{digits}", CultureInfo.InvariantCulture);

    // `again`: the pinned document is measured once more (a new view, a new measurement), and no new numbers are rolled.
    private async Task MeasureFlux(IBrowser browser, bool again)
    {
        Directory.CreateDirectory(fluxDir);
        string document;
        if (again)
        {
            document = await File.ReadAllTextAsync(Path.Combine(fluxDir, "document.html"));
        }
        else
        {
            using var http = new HttpClient();
            document = await http.GetStringAsync(new Uri(Url));
        }

        await File.WriteAllTextAsync(Path.Combine(fluxDir, "document.html"), document);
        // One document for both schemes: MeasurePage opens a context per scheme, and each is handed the same page.
        await using var replaying = new Replaying(browser, document);
        var boxes = new List<JsonArray>();
        foreach (var size in Views)
        {
            var (width, height) = (size[0], size[1]);
            var found = new JsonObject();
            var schemes = await Measure.MeasurePage(replaying, Url, fluxDir + Sized(size), async (page, scheme) =>
            {
                // Flux sizes a chart's gutters by measuring its tick labels when it first draws, and that can be before
                // Inter has loaded: the same chart then comes out 2.5px narrower in whichever scheme lost the race. It
                // draws again when its box changes, so the box is changed and put back once the fonts are in.
                await Redraw(page, width, height);
                found[scheme] = await page.Run(Geometry, "ui-chart");
            }, size);
            await File.WriteAllTextAsync(Path.Combine(fluxDir, $"measurements{Sized(size)}.json"), Js.Stringify(schemes));
            await File.WriteAllTextAsync(Path.Combine(fluxDir, $"geometry{Sized(size)}.json"), Js.Stringify(found));
            boxes.Add(found["light"] as JsonArray ?? []);
        }

        // Each example's rows, and the box each of its drawings was given in each view (`1280x900`, `390x844`).
        var data = new JsonArray();
        for (var index = 0; index < boxes[0].Count; index++)
        {
            var example = boxes[0][index]!;
            var perView = new JsonObject();
            for (var v = 0; v < Views.Length; v++)
            {
                perView[$"{Views[v][0]}x{Views[v][1]}"] = new JsonArray((boxes[v][index]!["charts"] as JsonArray ?? []).Select(chart => chart!["box"]?.DeepClone()).ToArray());
            }

            data.Add(new JsonObject
            {
                ["section"] = example["section"]?.DeepClone(),
                ["charts"] = new JsonArray((example["charts"] as JsonArray ?? []).Select(chart => chart!["value"]?.DeepClone()).ToArray()),
                ["boxes"] = perView,
            });
        }

        await File.WriteAllTextAsync(dataFile, Js.Stringify(data, 1) + "\n");
        Console.WriteLine($"flux chart parity: pinned {data.Count} examples -> {fluxDir} ({string.Join(", ", Views.Select(v => string.Join('x', v)))}), data -> {dataFile}");
    }

    private static async Task Redraw(IPage page, int width, int height)
    {
        await page.SetViewportSizeAsync(width - 100, height);
        await page.WaitForTimeoutAsync(400);
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForTimeoutAsync(400);
    }

    private static (List<string> Diffs, double Worst) Compare(JsonNode theirs, JsonNode? mine)
    {
        var diffs = new List<string>();
        var worst = 0.0;
        var theirCharts = theirs["charts"] as JsonArray ?? [];
        var myCharts = mine?["charts"] as JsonArray;
        if (myCharts is null || myCharts.Count != theirCharts.Count)
        {
            return ([$"charts: Flux has {theirCharts.Count}, Rask has {myCharts?.Count ?? 0}"], worst);
        }

        for (var c = 0; c < theirCharts.Count; c++)
        {
            var nodes = theirCharts[c]!["nodes"] as JsonArray ?? [];
            var others = myCharts[c]!["nodes"] as JsonArray ?? [];
            if (nodes.Count != others.Count)
            {
                diffs.Add(ParityComparer.Slice($"chart {c}: {nodes.Count} SVG nodes vs {others.Count} (<{string.Join(' ', nodes.Select(n => Js.Str(n!["tag"])))}> vs <{string.Join(' ', others.Select(n => Js.Str(n!["tag"])))}>)", 600));
                continue;
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                var (a, b) = (nodes[i]!, others[i]!);
                var (tagA, tagB) = (Js.Str(a["tag"]), Js.Str(b["tag"]));
                var (textA, textB) = (Js.Str(a["text"]) ?? "", Js.Str(b["text"]) ?? "");
                var (hiddenA, hiddenB) = (Js.Truthy(a["hidden"]), Js.Truthy(b["hidden"]));
                var where = $"chart {c} node {i} <{tagA}>{(textA.Length > 0 ? $" \"{textA}\"" : "")}";
                if (tagA != tagB)
                {
                    diffs.Add($"{where}: tag <{tagB}>");
                }

                if (textA != textB)
                {
                    diffs.Add($"{where}: text \"{textA}\" vs \"{textB}\"");
                }

                if (hiddenA != hiddenB)
                {
                    diffs.Add($"{where}: {(hiddenA ? "hidden in Flux" : "hidden in Rask")}");
                }

                if (!hiddenA && tagA == "text" && Js.Str(a["anchor"]) != Js.Str(b["anchor"]))
                {
                    diffs.Add($"{where}: text-anchor {Js.Str(a["anchor"])} vs {Js.Str(b["anchor"])}");
                }

                var attrsA = a["attrs"] as JsonObject ?? [];
                var attrsB = b["attrs"] as JsonObject ?? [];
                foreach (var name in attrsA.Select(p => p.Key).Concat(attrsB.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
                {
                    var (valueA, valueB) = (Js.Str(attrsA[name]), Js.Str(attrsB[name]));
                    var x = Numbers(valueA);
                    var y = Numbers(valueB);
                    if (x.Shape != y.Shape)
                    {
                        diffs.Add($"{where}: {name}: {ParityComparer.Slice(valueA ?? "undefined", 70)} vs {ParityComparer.Slice(valueB ?? "undefined", 70)}");
                        continue;
                    }

                    var off = x.Values.Select((v, k) => Math.Abs(v - y.Values[k])).Aggregate(0.0, Math.Max);
                    worst = Math.Max(worst, off);
                    if (off > Allowed(name))
                    {
                        diffs.Add($"{where}: {name} is off by {Fixed(off, 3)}px");
                    }
                }
            }
        }

        return (diffs, worst);
    }

    // An attribute as its commands and its numbers: "M 1,2 C3 4" and "M1 2C3,4" are the same path.
    private static (string Shape, List<double> Values) Numbers(string? value)
    {
        if (value is null)
        {
            return ("(none)", []);
        }

        var values = new List<double>();
        var shape = Number().Replace(value, n =>
        {
            values.Add(Js.Parse(n.Value));
            return "#";
        });
        shape = Command().Replace(Spaces().Replace(shape, " "), "$1").Trim();
        return (shape, values);
    }

    [GeneratedRegex(@"-?\d*\.?\d+(?:e[-+]?\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex Number();

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@" ?([a-zA-Z()]) ?")]
    private static partial Regex Command();

    // ---- The pointer walk ------------------------------------------------------------------------------------

    private async Task<int> PointerWalk(IBrowser browser, string raskPage)
    {
        var document = await File.ReadAllTextAsync(Path.Combine(fluxDir, "document.html"));
        var (width, height) = (view[0], view[1]);
        var context = await browser.NewContextAsync(Screen.Of(width, height));
        await context.RouteAsync(Url, route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html; charset=utf-8", Body = document }));
        var theirs = await context.NewPageAsync();
        await theirs.GotoAsync(Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        await theirs.Run("() => document.fonts.ready.then(() => undefined)");
        // Drawn again once the fonts are in, as --measure does.
        await Redraw(theirs, width, height);
        var ours = await context.NewPageAsync();
        await ours.GotoAsync(Repo.FileUrl(raskPage), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await ours.Run("() => document.fonts.ready.then(() => undefined)");
        if (Sized(view).Length > 0)
        {
            await Framed(ours);
        }

        await Runtime.With(ours, "rask-hooks.ts");

        var count = (int)Js.Number(await theirs.Run("() => document.querySelectorAll('[data-preview-wrapper] ui-chart').length"));
        var failed = 0;
        for (var chart = 0; chart < count; chart++)
        {
            var round = Js.Truthy(await theirs.Run("i => !!document.querySelectorAll('[data-preview-wrapper] ui-chart')[i].querySelector('template[name=pie], svg path[stroke-linejoin=round][stroke-width=\"3\"]')", chart));
            var differences = new List<string>();
            foreach (var at in (round ? Round : Across).Append([-0.2, -0.2]))
            {
                var (fx, fy) = (at[0], at[1]);
                var steps = await Task.WhenAll(Step(theirs, "ui-chart", chart, fx, fy), Step(ours, "[data-ui-chart]", chart, fx, fy));
                var off = Differing(steps[0]!, steps[1]!);
                if (off.Count > 0)
                {
                    differences.Add($"at {Js.Num(fx)},{Js.Num(fy)}: {string.Join("; ", off)}");
                }
            }

            failed += differences.Count;
            Console.WriteLine($"{(differences.Count > 0 ? "FAIL" : "ok  ")} pointer chart {chart}{(differences.Count > 0 ? $" — {differences.Count} step(s)" : "")}");
            foreach (var d in differences.Take(limit))
            {
                Console.WriteLine(ParityComparer.Slice($"       {d}", 500));
            }
        }

        await context.CloseAsync();
        return failed;
    }

    // In a viewport of its own each Rask example is given the room its Flux twin was given, as parity gives it: the twin's
    // width and padding, from the measurement of the pinned load in that view.
    private async Task Framed(IPage page)
    {
        var examples = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fluxDir, $"measurements{Sized(view)}.json")))!["light"];
        await page.Run("""
            examples => {
              const seen = {};
              for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) {
                const section = wrapper.dataset.section ?? '';
                const ordinal = seen[section] = (seen[section] ?? -1) + 1;
                const theirs = examples.find(example => example.section === section && example.ordinal === ordinal);
                if (!theirs) continue;
                for (const key of ['boxSizing', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft']) wrapper.style[key] = theirs.nodes[0].style[key];
                wrapper.style.width = `${theirs.size[0]}px`;
              }
            }
            """, examples?.DeepClone());
    }

    // Moves the pointer to a share of chart `index`'s drawing — in two moves, as a hand arrives — and reads the chart.
    private static async Task<JsonNode?> Step(IPage page, string selector, int index, double fx, double fy)
    {
        var box = (await page.Run("""
            ({ selector, index }) => {
              const chart = document.querySelectorAll(`[data-preview-wrapper] ${selector}`)[index];
              chart.scrollIntoView({ block: 'center' });
              const svg = [...chart.querySelectorAll('svg')].find(s => !s.closest('template')).getBoundingClientRect();
              return { x: svg.left, y: svg.top, width: svg.width, height: svg.height };
            }
            """, new { selector, index }))!;
        var x = Js.Round(Js.Number(box["x"]) + (fx * Js.Number(box["width"])));
        var y = Js.Round(Js.Number(box["y"]) + (fy * Js.Number(box["height"])));
        await page.Mouse.MoveAsync((float)(x - 1), (float)y);
        await page.Mouse.MoveAsync((float)x, (float)y);
        await page.Run("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
        return await page.Run(Read, new { selector, index });
    }

    private static List<string> Differing(JsonNode a, JsonNode b)
    {
        var off = new List<string>();
        static double[] Numbers(JsonNode? node) => (node as JsonArray ?? []).Select(Js.Number).ToArray();
        static bool Close(double[] x, double[] y) => x.Length == y.Length && x.Select((v, k) => Math.Abs(v - y[k]) <= Near).All(near => near);
        static string Tenths(double[] values) => string.Join(',', values.Select(v => Fixed(v, 1)));

        var (tipA, tipB) = (a["tooltip"], b["tooltip"]);
        if ((tipA is null) != (tipB is null))
        {
            off.Add($"tooltip {(tipA is not null ? "shown" : "hidden")} in Flux, {(tipB is not null ? "shown" : "hidden")} in Rask");
        }
        else if (tipA is not null)
        {
            if (Js.Str(tipA["text"]) != Js.Str(tipB!["text"]))
            {
                off.Add($"tooltip says \"{Js.Str(tipA["text"])}\" vs \"{Js.Str(tipB["text"])}\"");
            }

            var (atA, atB) = (Numbers(tipA["at"]), Numbers(tipB["at"]));
            if (!Close(atA, atB))
            {
                off.Add($"tooltip at {Tenths(atA)} vs {Tenths(atB)}");
            }
        }

        var (cursorA, cursorB) = (a["cursor"], b["cursor"]);
        if ((cursorA is null) != (cursorB is null))
        {
            off.Add($"cursor {(cursorA is not null ? "shown" : "hidden")} in Flux, {(cursorB is not null ? "shown" : "hidden")} in Rask");
        }
        else if (cursorA is not null && !Close(Numbers(cursorA), Numbers(cursorB)))
        {
            off.Add($"cursor box {Tenths(Numbers(cursorA))} vs {Tenths(Numbers(cursorB))}");
        }

        foreach (var what in new[] { "summary", "points", "slices" })
        {
            if (Js.Str(a[what]) != Js.Str(b[what]))
            {
                off.Add($"{what} \"{Js.Str(a[what])}\" vs \"{Js.Str(b[what])}\"");
            }
        }

        return off;
    }

    // A browser whose every context answers the chart page with the pinned document: what MeasurePage is handed, so both
    // schemes draw the same numbers. Everything else is the browser's own.
    private sealed class Replaying(IBrowser browser, string document) : IBrowser
    {
        public event EventHandler<IBrowser>? Disconnected
        {
            add => browser.Disconnected += value;
            remove => browser.Disconnected -= value;
        }

        public IBrowserType BrowserType => browser.BrowserType;

        public IReadOnlyList<IBrowserContext> Contexts => browser.Contexts;

        public bool IsConnected => browser.IsConnected;

        public string Version => browser.Version;

        public async Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
        {
            var context = await browser.NewContextAsync(options);
            await context.RouteAsync(Url, route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html; charset=utf-8", Body = document }));
            return context;
        }

        public Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null) => browser.NewPageAsync(options);

        public Task<ICDPSession> NewBrowserCDPSessionAsync() => browser.NewBrowserCDPSessionAsync();

        public event EventHandler<IBrowserContext>? Context
        {
            add => browser.Context += value;
            remove => browser.Context -= value;
        }

        public Task<BrowserBindResult> BindAsync(string title, BrowserBindOptions? options = null) => browser.BindAsync(title, options);

        public Task UnbindAsync() => browser.UnbindAsync();

        public Task CloseAsync(BrowserCloseOptions? options = null) => browser.CloseAsync(options);

        // The browser is the caller's: it outlives this view of it.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
