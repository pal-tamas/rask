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
    private readonly List<EditOp> _ops = new(64);
    private readonly FrameDiffer.DiffScratch _scratch = new();
    private RenderFrame[] _afterReorder = null!;
    private string _afterReorderHtml = "";
    private RenderFrame[] _afterReverse = null!;
    private string _afterReverseHtml = "";
    private RenderFrame[] _afterText = null!;

    // Realistic partial-shuffle shapes the 2-swap / full-reverse pair didn't cover. TopNRerank
    // reverses just the first 10 rows (a table sort that only reranks the head) — a localized
    // permutation with a near-full LIS, so only ~10 rows are off-LIS and the move loop stays
    // sub-quadratic. AppendWithDeletes drops every 10th row and appends the same number of new keys
    // at the tail (a feed/log churn) — survivors keep order, so it's removes + inserts, not moves.
    private RenderFrame[] _afterTopN = null!;
    private string _afterTopNHtml = "";
    private RenderFrame[] _afterAppendDel = null!;
    private string _afterAppendDelHtml = "";

    private RenderFrame[] _before = null!;

    // Insert scenario: a sparse list (even keys only) grows into the full list, so every odd
    // key is an InsertSubtree carrying its freshly-sliced HTML fragment. _fullHtml is the diff's
    // newHtml source, so each insert op runs FrameDiffer.SliceHtml (one Substring per inserted row).
    private RenderFrame[] _beforeSparse = null!;
    private RenderFrame[] _afterFull = null!;
    private string _fullHtml = "";

    [Params(100, 1000)] public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var order = new int[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            order[i] = i;
        }

        _before = FramesOf(BuildKeyedList(order, -1));

        // Reorder: swap two entries (same shape as PayloadBytesPerUpdate.KeyedList100Reorder).
        var reordered = (int[])order.Clone();
        (reordered[5], reordered[RowCount - 5]) = (reordered[RowCount - 5], reordered[5]);
        (_afterReorder, _afterReorderHtml) = FramesAndHtmlOf(BuildKeyedList(reordered, -1));

        // Full reverse: the worst case for the keyed move loop. A reversed permutation has an LIS
        // of length 1, so n-1 rows are off-LIS and each emits a move — the loop's per-move
        // live.IndexOf + live.Insert (each O(n)) then makes the whole step O(n²). The two-swap
        // Reorder above only ever moves a couple of rows, so it never surfaced this cost.
        var reversed = (int[])order.Clone();
        Array.Reverse(reversed);
        (_afterReverse, _afterReverseHtml) = FramesAndHtmlOf(BuildKeyedList(reversed, -1));

        // Same key order, one kept row's inner text changed — exercises the keyed step-5
        // inner-diff recursion (the path that re-enters DiffSiblings under a keyed parent).
        _afterText = FramesOf(BuildKeyedList(order, RowCount / 2));

        // Sparse → full: even keys present before, all keys after, so the odd keys insert.
        var evenOrder = new int[(RowCount + 1) / 2];
        for (var i = 0; i < evenOrder.Length; i++)
        {
            evenOrder[i] = i * 2;
        }

        _beforeSparse = FramesOf(BuildKeyedList(evenOrder, -1));
        (_afterFull, _fullHtml) = FramesAndHtmlOf(BuildKeyedList(order, -1));

        // Top-N rerank: reverse only the first 10 rows; the rest keep their order.
        var topN = (int[])order.Clone();
        Array.Reverse(topN, 0, Math.Min(10, RowCount));
        (_afterTopN, _afterTopNHtml) = FramesAndHtmlOf(BuildKeyedList(topN, -1));

        // Append-with-deletes: drop every 10th key, append that many fresh keys at the tail.
        var kept = new List<int>(RowCount);
        var removed = 0;
        for (var i = 0; i < RowCount; i++)
        {
            if (i % 10 == 9)
            {
                removed++;
                continue;
            }

            kept.Add(order[i]);
        }

        for (var i = 0; i < removed; i++)
        {
            kept.Add(RowCount + i); // brand-new keys → InsertSubtree at the tail
        }

        (_afterAppendDel, _afterAppendDelHtml) = FramesAndHtmlOf(BuildKeyedList(kept.ToArray(), -1));
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
    public int ReverseReorder_ReusedScratch()
    {
        // Worst-case keyed reorder: ~n moves, so this is the benchmark that actually exercises the
        // O(n²) move loop (live.IndexOf + live.Insert per off-LIS row).
        _ops.Clear();
        FrameDiffer.Diff(_before, _afterReverse, _ops, _scratch, out _, _afterReverseHtml);
        return _ops.Count;
    }

    [Benchmark]
    public int TopNRerank_ReusedScratch()
    {
        // Localized permutation: only the first 10 rows move, so the LIS is ~n and the move loop
        // touches a handful of rows — the realistic table-sort case that stays well sub-quadratic.
        _ops.Clear();
        FrameDiffer.Diff(_before, _afterTopN, _ops, _scratch, out _, _afterTopNHtml);
        return _ops.Count;
    }

    [Benchmark]
    public int AppendWithDeletes_ReusedScratch()
    {
        // Feed/log churn: every 10th row removed, the same count of new keys appended. Survivors keep
        // order (no moves), so this measures keyed remove + tail-insert reconcile, not the move loop.
        _ops.Clear();
        FrameDiffer.Diff(_before, _afterAppendDel, _ops, _scratch, out _, _afterAppendDelHtml);
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

    [Benchmark]
    public int InsertRows_ReusedScratch()
    {
        // Every odd key inserts, so this measures the per-InsertSubtree HTML SliceHtml cost
        // (one Substring of _fullHtml per inserted row) on top of the keyed reconcile.
        _ops.Clear();
        FrameDiffer.Diff(_beforeSparse, _afterFull, _ops, _scratch, out _, _fullHtml);
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
