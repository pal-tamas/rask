using System.Buffers;
using System.Globalization;
using System.Text;
using Rask.Core;
using Rask.Core.Live;
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
internal static class PayloadBytesReport
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
                BuildKeyedListTree(MakeKeyedOrder(101)))
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
        // Full-HTML payload size — what the server ships TODAY for every state change.
        var html = after.RenderAsLiveRoot();
        writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8WithRoot(writer, html, "session-bench", null, false);
        var fullBytes = writer.WrittenCount;

        // Diff payload size — what the server WILL ship once the diff codec wires in.
        // Build the previous-render frame stream + current-render frame stream, diff them,
        // serialize as the diff wire format. Pass `html` so InsertSubtree ops carry
        // their HTML fragment (key for keyed-list / structural-change scenarios).
        var beforeFrames = CaptureFrames(before);
        var afterFrames = CaptureFrames(after);
        var ops = new List<EditOp>();
        FrameDiffer.Diff(beforeFrames, afterFrames, ops, html);

        writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(writer, ops);
        var diffBytes = writer.WrittenCount;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3}",
            name, fullBytes, diffBytes, ops.Count));

        return new Row(name, fullBytes, diffBytes, ops.Count);
    }

    private static RenderFrame[] CaptureFrames(Component tree)
    {
        var sb = new StringBuilder();
        var fw = new FrameWriter();
        using (FrameSinkScope.Push(fw))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        return fw.WrittenSpan.ToArray();
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
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;", Key: i)[
                C.Span(Class: "label")[$"Item {i}"],
                C.A($"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32),
                C.Input<string>(InputType.Text, $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return [
            C.Doctype(),
            C.Html()[
                C.Body()[
                    C.Div(Class: "container", Id: "root")[
                        C.Div(Class: "counter", Id: "counter")[C.Span(Class: "value")[counter.ToString()]],
                        C.Div(Class: "body")[rows]
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
            rows.Add(C.Div(
                Class: "row",
                Data: new Dictionary<string, string?> { ["rask-key"] = idx.ToString() })[
                C.Span()[$"Item {idx}"]
            ]);
        }

        return [
            C.Doctype(),
            C.Html()[C.Body()[C.Div(Class: "list")[rows]]]
        ];
    }

    private static Component BuildLargePageWithDeepTextCell(int counter)
    {
        const int rowCount = 200;
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var text = i == rowCount / 2 ? $"ticker {counter}" : $"Item {i}";
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;", Key: i)[
                C.Span(Class: "label")[text],
                C.A($"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32),
                C.Input<string>(InputType.Text, $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return [
            C.Doctype(),
            C.Html()[
                C.Body()[C.Div(Class: "body")[rows]]
            ]
        ];
    }
}
