using System.Text;
using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// Isolates the FrameDiffer keyed-reconciliation cost. The before/after frame streams are
// pre-built in [GlobalSetup], so each [Benchmark] body measures ONLY the diff — no tree
// build, no serialize. That makes the scratch-pooling allocation win visible independently
// of the per-iteration tree-build cost that dominates PayloadBytesPerUpdate.
//
// The Reused vs Fresh pair is the headline comparison:
//   - Reused threads ONE DiffScratch across calls — the steady-state per-session path
//     (SessionRenderCache owns the scratch). The keyed reconcile allocates only the EditOp
//     path arrays (intentional wire data); the key maps, surviving/live lists, the LIS set,
//     and the permutation buffers are pooled / ArrayPool-rented.
//   - Fresh allocates a DiffScratch per call — approximates the pre-pooling profile (every
//     keyed parent rebuilding its working set from scratch).
[MemoryDiagnoser]
public class FrameDifferBenchmarks
{
    [Params(100, 1000)] public int RowCount { get; set; }

    private RenderFrame[] _before = null!;
    private RenderFrame[] _afterReorder = null!;
    private RenderFrame[] _afterText = null!;
    private string _afterReorderHtml = "";
    private readonly List<EditOp> _ops = new(64);
    private readonly FrameDiffer.DiffScratch _scratch = new();

    [GlobalSetup]
    public void Setup()
    {
        var order = new int[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            order[i] = i;
        }

        _before = FramesOf(BuildKeyedList(order, textRow: -1));

        // Reorder: swap two entries (same shape as PayloadBytesPerUpdate.KeyedList100Reorder).
        var reordered = (int[])order.Clone();
        (reordered[5], reordered[RowCount - 5]) = (reordered[RowCount - 5], reordered[5]);
        (_afterReorder, _afterReorderHtml) = FramesAndHtmlOf(BuildKeyedList(reordered, textRow: -1));

        // Same key order, one kept row's inner text changed — exercises the keyed step-5
        // inner-diff recursion (the path that re-enters DiffSiblings under a keyed parent).
        _afterText = FramesOf(BuildKeyedList(order, textRow: RowCount / 2));
    }

    [Benchmark(Baseline = true)]
    public int Reorder_ReusedScratch()
    {
        _ops.Clear();
        FrameDiffer.Diff(_before, _afterReorder, _ops, _scratch, out _, _afterReorderHtml);
        return _ops.Count;
    }

    [Benchmark]
    public int Reorder_FreshScratch()
    {
        _ops.Clear();
        FrameDiffer.Diff(_before, _afterReorder, _ops, new FrameDiffer.DiffScratch(), out _, _afterReorderHtml);
        return _ops.Count;
    }

    [Benchmark]
    public int NoChange_ReusedScratch()
    {
        // Identical streams: 0 ops, but still rents a bundle and populates the key maps —
        // measures the per-keyed-parent collection overhead with nothing to emit.
        _ops.Clear();
        FrameDiffer.Diff(_before, _before, _ops, _scratch, out _);
        return _ops.Count;
    }

    [Benchmark]
    public int TextInKeptRow_ReusedScratch()
    {
        _ops.Clear();
        FrameDiffer.Diff(_before, _afterText, _ops, _scratch, out _);
        return _ops.Count;
    }

    private static Component BuildKeyedList(int[] order, int textRow)
    {
        var rows = new List<Child>(order.Length);
        for (var i = 0; i < order.Length; i++)
        {
            var idx = order[i];
            // textRow flips one row's inner text so the text scenario has a single deep
            // change to recurse into; -1 leaves every row stable.
            var label = idx == textRow ? $"Item {idx}!" : $"Item {idx}";
            rows.Add(C.Div(Key: idx)[C.Span()[label]]);
        }

        return C.Div(Class: "list")[rows];
    }

    private static RenderFrame[] FramesOf(Component tree) => FramesAndHtmlOf(tree).Frames;

    private static (RenderFrame[] Frames, string Html) FramesAndHtmlOf(Component tree)
    {
        var writer = new FrameWriter();
        var sb = new StringBuilder(8192);
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        var span = writer.WrittenSpan;
        var copy = new RenderFrame[span.Length];
        span.CopyTo(copy);
        return (copy, sb.ToString());
    }
}
