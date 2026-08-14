using System.Buffers;
using System.Globalization;
using System.Text;
using Rask.Benchmarks.Infrastructure;
using Rask.Core;
using Rask.Core.Live;
using BI = Rask.Benchmarks.Infrastructure.Generated;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// One-shot report (NOT a BenchmarkDotNet benchmark) that prints the *headline*
// metric for the Rask perf pass: how many bytes go over the wire for one
// StateHasChanged in three representative scenarios. The number is deterministic
// today (no measurement noise — every render emits the same payload shape with a
// single small value differing), so a single build per scenario is enough.
//
// Invoke:
//   dotnet run -c Release --project Rask.Benchmarks -- payload-bytes
//
// After Phase 1 (diff layer) the same scenarios should drop to:
//   CounterOnLargePage  ≤ 200 bytes
//   KeyedList100Reorder ≤ 500 bytes
//   TextNodeUpdate      ≤ 100 bytes
// — the targets that justify the "Rask is a real Blazor competitor" claim.
[global::Rask.Core.RaskMarkup]
internal static partial class PayloadBytesReport
{
    private const string Header = "Scenario,FullPayloadBytes,DiffPayloadBytes,DiffOpCount";

    private readonly record struct Row(string Scenario, int FullBytes, int DiffBytes, int DiffOps);

    public static int Run(string[] args)
    {
        var writer = new ArrayBufferWriter<byte>(64 * 1024);
        var check = Array.Exists(args, a => a == "--check");

        Console.WriteLine(Header);

        var rows = new List<Row>
        {
            Report("CounterOnLargePage", writer,
                BuildLargePageWithCounter(1),
                BuildLargePageWithCounter(2)),
            Report("KeyedList100Reorder", writer,
                BuildKeyedListTree(MakeKeyedOrder(100)),
                BuildKeyedListTree(SwapKeyed(MakeKeyedOrder(100), 5, 95))),
            Report("TextNodeUpdate", writer,
                BuildLargePageWithDeepTextCell(1),
                BuildLargePageWithDeepTextCell(2)),
            // Structural change: append one row to a 100-row list. Triggers an
            // InsertSubtree op with the new row's HTML fragment as op.Value.
            Report("AppendRowToList100", writer,
                BuildKeyedListTree(MakeKeyedOrder(100)),
                BuildKeyedListTree(MakeKeyedOrder(101))),
            // Raw-tainted guide page: a sample container mixes a Raw code block with a sibling status
            // node whose text changes. Pre-fix this shipped the WHOLE document (full-HTML morph); now it
            // ships one scoped MorphSubtree carrying just the sample container's inner HTML — the diff
            // drops from full-page to one subtree.
            Report("RawGuidePage", writer,
                BuildGuideDocument(1),
                BuildGuideDocument(2)),
            // Handler-count shift: a 100-row list of buttons with one conditional button ABOVE it, toggled
            // between the two renders. The rows themselves do not change at all — only the toolbar does —
            // so the ideal diff touches the toolbar and nothing else. This is the one scenario rendered
            // through the LIVE root (the others serialize without a live context, which registers no
            // handlers at all), because handler ids only exist on that path.
            ReportLive("HandlerShiftAboveList100", writer,
                BI.HandlerShiftPage(RowCount: 100),
                page => page.ShowToolbarAction = true)
        };

        return check ? CheckAgainstBaseline(rows) : 0;
    }

    // Compares the deterministic diff-codec metrics (diff wire bytes + op count — the values that only
    // move when the render/diff path itself changes) against the committed baseline, and fails the CI
    // gate on a regression. FullPayloadBytes is informational (it moves whenever the scenario markup
    // changes) and is not gated. An improvement (fewer bytes/ops) passes but asks for a baseline refresh
    // so the file keeps tracking reality; a scenario missing from the baseline also fails, so a new
    // scenario can't slip in ungated.
    private static int CheckAgainstBaseline(List<Row> current)
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Baselines", "payload-bytes.csv");
        if (!File.Exists(baselinePath))
        {
            Console.Error.WriteLine($"::error::Baseline not found at {baselinePath}");
            return 1;
        }

        var baseline = ParseBaseline(baselinePath);

        var regressed = false;
        var improved = false;
        Console.WriteLine();
        Console.WriteLine("Regression check vs Baselines/payload-bytes.csv (diff bytes / op count):");
        foreach (var row in current)
        {
            if (!baseline.TryGetValue(row.Scenario, out var b))
            {
                Console.Error.WriteLine(
                    $"::error::Scenario '{row.Scenario}' is missing from the baseline — " +
                    "add it (regenerate with `payload-bytes`) so it can't regress ungated.");
                regressed = true;
                continue;
            }

            var byteDelta = row.DiffBytes - b.DiffBytes;
            var opDelta = row.DiffOps - b.DiffOps;
            var status = byteDelta > 0 || opDelta > 0 ? "REGRESSED" : byteDelta < 0 || opDelta < 0 ? "improved" : "ok";
            Console.WriteLine(
                $"  {row.Scenario,-22} diff {row.DiffBytes,6}B ({byteDelta,+0}) " +
                $"ops {row.DiffOps} ({opDelta,+0})  [{status}]");

            if (byteDelta > 0 || opDelta > 0)
            {
                Console.Error.WriteLine(
                    $"::error::{row.Scenario} regressed: diff bytes {b.DiffBytes}→{row.DiffBytes}, " +
                    $"ops {b.DiffOps}→{row.DiffOps}.");
                regressed = true;
            }
            else if (byteDelta < 0 || opDelta < 0)
            {
                improved = true;
            }
        }

        if (improved && !regressed)
        {
            Console.WriteLine(
                "::notice::Diff payload improved vs baseline — refresh Baselines/payload-bytes.csv " +
                "(`dotnet run -c Release --project benchmarks/Rask.Benchmarks -- payload-bytes`) so it keeps tracking reality.");
        }

        return regressed ? 1 : 0;
    }

    private static Dictionary<string, Row> ParseBaseline(string path)
    {
        var map = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith("Scenario,", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split(',');
            if (cells.Length < 4)
            {
                continue;
            }

            map[cells[0]] = new Row(
                cells[0],
                int.Parse(cells[1], CultureInfo.InvariantCulture),
                int.Parse(cells[2], CultureInfo.InvariantCulture),
                int.Parse(cells[3], CultureInfo.InvariantCulture));
        }

        return map;
    }

    private static Row Report(
        string name,
        ArrayBufferWriter<byte> writer,
        Component before,
        Component after)
    {
        // Diff payload size — build the previous- and current-render frame streams, diff them, and
        // serialize as the diff wire format. The frames' HtmlStart/HtmlEnd offsets index into the SAME
        // serialized string (afterHtml) they were captured against, so that string — not the injected
        // RenderAsLiveRoot output — is the slice source for InsertSubtree / MorphSubtree fragments,
        // mirroring how the live session pairs its frames with its render HTML.
        var (beforeFrames, _) = CaptureFrames(before);
        var (afterFrames, afterHtml) = CaptureFrames(after);

        // Full-HTML size is measured from the live-root render (runtime script and all) — what the
        // server would ship without the diff codec — while the diff is sliced from the bare serialize
        // the frames were captured against.
        return Emit(name, writer, beforeFrames, afterFrames, afterHtml, after.RenderAsLiveRoot());
    }

    /// <summary>
    ///     Diff cost for one state change on a PERSISTENT root, rendered through <c>RenderAsLiveRoot</c> so
    ///     the live render context exists and event handlers are actually registered. <see cref="Report" />
    ///     builds two independent trees and serializes them bare, which is right for markup-shaped
    ///     scenarios but registers no handlers — so it can't see anything handler ids do.
    /// </summary>
    private static Row ReportLive<T>(
        string name,
        ArrayBufferWriter<byte> writer,
        T page,
        Action<T> mutate)
        where T : Component
    {
        var (beforeFrames, _) = CaptureLiveFrames(page);
        mutate(page);
        // Here the live-root render IS what the frames were captured against, so one string serves both.
        var (afterFrames, afterHtml) = CaptureLiveFrames(page);
        return Emit(name, writer, beforeFrames, afterFrames, afterHtml, afterHtml);
    }

    // Shared tail: size the full payload and the diff for one before/after frame pair, print the CSV
    // row, and hand it back for the baseline check.
    private static Row Emit(
        string name,
        ArrayBufferWriter<byte> writer,
        RenderFrame[] beforeFrames,
        RenderFrame[] afterFrames,
        string afterHtml,
        string fullHtml)
    {
        writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8WithRoot(writer, fullHtml, "session-bench", null, false);
        var fullBytes = writer.WrittenCount;

        var ops = new List<EditOp>();
        FrameDiffer.Diff(beforeFrames, afterFrames, ops, afterHtml);

        writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(writer, ops, newHtml: afterHtml);
        var diffBytes = writer.WrittenCount;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3}",
            name, fullBytes, diffBytes, ops.Count));

        return new Row(name, fullBytes, diffBytes, ops.Count);
    }

    private static (RenderFrame[] Frames, string Html) CaptureLiveFrames(Component root)
    {
        var fw = new FrameWriter();
        string html;
        using (FrameSinkScope.Push(fw))
        {
            html = root.RenderAsLiveRoot();
        }

        return (fw.WrittenSpan.ToArray(), html);
    }

    private static (RenderFrame[] Frames, string Html) CaptureFrames(Component tree)
    {
        var sb = new StringBuilder();
        var fw = new FrameWriter();
        using (FrameSinkScope.Push(fw))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        return (fw.WrittenSpan.ToArray(), sb.ToString());
    }

    private static int[] SwapKeyed(int[] order, int a, int b)
    {
        (order[a], order[b]) = (order[b], order[a]);
        return order;
    }

    private static int[] MakeKeyedOrder(int n)
    {
        var order = new int[n];
        for (var i = 0; i < n; i++)
        {
            order[i] = i;
        }

        return order;
    }

    private static Component BuildLargePageWithCounter(int counter)
    {
        const int rowCount = 200;
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(Div.Class("row").Id($"r{i}").Style("display:flex;gap:8px;").Key(i)[
                Span.Class("label")[$"Item {i}"],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"],
                Img.Src($"/img/{i}.png").Alt($"item {i}").Width(32).Height(32),
                Input.Value($"v{i}").Type(InputType.Text).Name($"f{i}").Placeholder("edit").MaxLength(64)
            ]);
        }

        return [
            Doctype,
            Html[
                Body[
                    Div.Class("container").Id("root")[
                        Div.Class("counter").Id("counter")[Span.Class("value")[counter.ToString()]],
                        Div.Class("body")[rows]
                    ]
                ]
            ]
        ];
    }

    private static Component BuildKeyedListTree(int[] order)
    {
        var rows = new List<Component>(order.Length);
        for (var i = 0; i < order.Length; i++)
        {
            var idx = order[i];
            rows.Add(Div
                .Class("row")
                .Data(new Dictionary<string, string?> { ["rask-key"] = idx.ToString() })[
                Span[$"Item {idx}"]
            ]);
        }

        return [
            Doctype,
            Html[Body[Div.Class("list")[rows]]]
        ];
    }

    // A guide/CodeSample page: a nav + sidebar shell plus a "sample" container that mixes a Raw code
    // block with a sibling status node. Only the status text changes between renders — a Raw-tainted
    // level, so the diff ships one scoped MorphSubtree carrying the sample container's inner HTML
    // (the code + status), never the whole document.
    private static Component BuildGuideDocument(int status)
    {
        const string highlightedCode =
            "<span class=\"k\">public</span> <span class=\"k\">class</span> <span class=\"t\">Counter</span>" +
            " : <span class=\"t\">Component</span> { <span class=\"k\">public</span> <span class=\"k\">int</span>" +
            " <span class=\"p\">Count</span> { <span class=\"k\">get</span>; <span class=\"k\">set</span>; }" +
            " <span class=\"k\">public</span> <span class=\"k\">override</span> <span class=\"t\">Component</span>" +
            " <span class=\"m\">Render</span>() =&gt; <span class=\"m\">Div</span>()[<span class=\"m\">Span</span>()" +
            "[<span class=\"p\">Count</span>.<span class=\"m\">ToString</span>()]]; }";

        var nav = new List<Component>(12);
        for (var i = 0; i < 12; i++)
        {
            nav.Add(A.Href($"/guides/{i}").Class("nav-link").Key(i)[$"Guide {i}"]);
        }

        return [
            Doctype,
            Html[
                Body[
                    Nav.Class("sidebar")[nav],
                    Main.Class("content")[
                        H1["Counter guide"],
                        P["A counter component highlighted below, with a live status line."],
                        Div.Class("sample").Id("sample")[
                            Raw.Value(highlightedCode),
                            Div.Class("status").Id("demo-status")[$"count: {status}"]
                        ]
                    ]
                ]
            ]
        ];
    }

    private static Component BuildLargePageWithDeepTextCell(int counter)
    {
        const int rowCount = 200;
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var text = i == rowCount / 2 ? $"ticker {counter}" : $"Item {i}";
            rows.Add(Div.Class("row").Id($"r{i}").Style("display:flex;gap:8px;").Key(i)[
                Span.Class("label")[text],
                A.Href($"/item/{i}").Class("lnk")[$"open {i}"],
                Img.Src($"/img/{i}.png").Alt($"item {i}").Width(32).Height(32),
                Input.Value($"v{i}").Type(InputType.Text).Name($"f{i}").Placeholder("edit").MaxLength(64)
            ]);
        }

        return [
            Doctype,
            Html[
                Body[Div.Class("body")[rows]]
            ]
        ];
    }
}
