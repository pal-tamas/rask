using System.Buffers;
using System.Globalization;
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
    public static int Run(string[] args)
    {
        var writer = new ArrayBufferWriter<byte>(64 * 1024);

        Console.WriteLine("Scenario,FullPayloadBytes,DiffPayloadBytes,DiffOpCount");

        Report("CounterOnLargePage", writer,
            BuildLargePageWithCounter(1),
            BuildLargePageWithCounter(2));
        Report("KeyedList100Reorder", writer,
            BuildKeyedListTree(MakeKeyedOrder(100)),
            BuildKeyedListTree(SwapKeyed(MakeKeyedOrder(100), 5, 95)));
        Report("TextNodeUpdate", writer,
            BuildLargePageWithDeepTextCell(1),
            BuildLargePageWithDeepTextCell(2));
        // Structural change: append one row to a 100-row list. Triggers an
        // InsertSubtree op with the new row's HTML fragment as op.Value.
        Report("AppendRowToList100", writer,
            BuildKeyedListTree(MakeKeyedOrder(100)),
            BuildKeyedListTree(MakeKeyedOrder(101)));

        return 0;
    }

    private static void Report(
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
        LivePayload.BuildPayloadUtf8Diff(writer, ops, null, false);
        var diffBytes = writer.WrittenCount;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3}",
            name, fullBytes, diffBytes, ops.Count));
    }

    private static RenderFrame[] CaptureFrames(Component tree)
    {
        var sb = new System.Text.StringBuilder();
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
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;")[
                C.Span(Class: "label")[$"Item {i}"],
                C.A($"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32),
                C.Input("text", $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return C.Fragment()[
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
        var rows = new List<Child>(order.Length);
        for (var i = 0; i < order.Length; i++)
        {
            var idx = order[i];
            rows.Add(C.Div(
                Class: "row",
                Data: new Dictionary<string, string?> { ["rask-key"] = idx.ToString() })[
                C.Span()[$"Item {idx}"]
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[C.Body()[C.Div(Class: "list")[rows]]]
        ];
    }

    private static Component BuildLargePageWithDeepTextCell(int counter)
    {
        const int rowCount = 200;
        var rows = new List<Child>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var text = i == rowCount / 2 ? $"ticker {counter}" : $"Item {i}";
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Style: "display:flex;gap:8px;")[
                C.Span(Class: "label")[text],
                C.A($"/item/{i}", Class: "lnk")[$"open {i}"],
                C.Img($"/img/{i}.png", $"item {i}", 32, 32),
                C.Input("text", $"f{i}", $"v{i}", "edit", MaxLength: 64)
            ]);
        }

        return C.Fragment()[
            C.Doctype(),
            C.Html()[
                C.Body()[C.Div(Class: "body")[rows]]
            ]
        ];
    }
}
