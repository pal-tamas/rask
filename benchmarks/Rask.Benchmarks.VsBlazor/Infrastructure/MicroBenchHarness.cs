using System.Text;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks.VsBlazor.Infrastructure;

/// <summary>
///     Pre-built-input helpers for the Micro_* benchmarks. Each call here belongs in
///     <c>[GlobalSetup]</c> so the per-iteration body executes only the hot path under
///     test (no Component construction, no FrameSinkScope plumbing, no HTML build).
///     Same intent as the rest of the suite's harness pattern in <see cref="RaskHarness" />,
///     just lowered to the layer below the render pipeline.
/// </summary>
public static class MicroBenchHarness
{
    public enum LisShape
    {
        /// <summary>arr[i] = i — already-sorted; LIS = full array. Best case.</summary>
        Identity,

        /// <summary>arr[i] = n - 1 - i — reverse sorted; LIS = 1. Worst case.</summary>
        Reverse,

        /// <summary>Seeded LCG permutation. Average case.</summary>
        RandomPermutation,

        /// <summary>Identity with element[1] swapped with element[n-2]. Realistic single-move shape.</summary>
        OneOutOfOrder
    }

    /// <summary>
    ///     Serialize <paramref name="tree" /> once under a fresh FrameSinkScope and
    ///     return a stable copy of the resulting RenderFrame stream. The returned
    ///     array survives the FrameWriter's pooled buffer being returned, so callers
    ///     can hold it across iterations without aliasing trouble.
    /// </summary>
    public static RenderFrame[] BuildFrames(Component tree)
    {
        var writer = new FrameWriter();
        var sb = new StringBuilder(4096);
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        var span = writer.WrittenSpan;
        var copy = new RenderFrame[span.Length];
        span.CopyTo(copy);
        return copy;
    }

    /// <summary>
    ///     Serialize <paramref name="tree" /> once and return both the frame stream and
    ///     the HTML the diff codec would attach to InsertSubtree ops (via the
    ///     frame-level HtmlStart/HtmlEnd offsets).
    /// </summary>
    public static (RenderFrame[] Frames, string Html) BuildFramesAndHtml(Component tree)
    {
        var writer = new FrameWriter();
        var sb = new StringBuilder(4096);
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        var span = writer.WrittenSpan;
        var copy = new RenderFrame[span.Length];
        span.CopyTo(copy);
        return (copy, sb.ToString());
    }

    /// <summary>
    ///     Run
    ///     <see
    ///         cref="FrameDiffer.Diff(System.ReadOnlySpan{RenderFrame},System.ReadOnlySpan{RenderFrame},List{EditOp},string?)" />
    ///     once and return the materialized op list. Used to seed Micro_LivePayloadBuildDiff
    ///     so the payload benchmark measures only the JSON encoder, not the differ.
    /// </summary>
    public static List<EditOp> BuildOps(RenderFrame[] before, RenderFrame[] after, string? newHtml = null)
    {
        var ops = new List<EditOp>(32);
        FrameDiffer.Diff(before, after, ops, newHtml);
        return ops;
    }

    /// <summary>
    ///     Build the input array for the LIS micro-benchmark. Reproducible — the LCG
    ///     seed is fixed (matches the Reports/KeyedListDiffDump permutation pattern)
    ///     so reruns produce identical inputs and the BDN numbers are comparable
    ///     across baseline pinnings.
    /// </summary>
    public static int[] BuildLisInput(int n, LisShape shape)
    {
        var arr = new int[n];
        switch (shape)
        {
            case LisShape.Identity:
                for (var i = 0; i < n; i++)
                {
                    arr[i] = i;
                }

                break;
            case LisShape.Reverse:
                for (var i = 0; i < n; i++)
                {
                    arr[i] = n - 1 - i;
                }

                break;
            case LisShape.OneOutOfOrder:
                for (var i = 0; i < n; i++)
                {
                    arr[i] = i;
                }

                if (n >= 3)
                {
                    (arr[1], arr[n - 2]) = (arr[n - 2], arr[1]);
                }

                break;
            case LisShape.RandomPermutation:
                for (var i = 0; i < n; i++)
                {
                    arr[i] = i;
                }

                // Fisher-Yates with a fixed-seed LCG for determinism. Same seed as
                // KeyedListDiffDump uses so the LIS micro numbers can be cross-referenced
                // against the keyed-list wire-bytes dump.
                var state = 0xC0FFEE_DEADBEEFUL;
                for (var i = n - 1; i > 0; i--)
                {
                    state = (state * 6364136223846793005UL) + 1442695040888963407UL;
                    var j = (int)((state >> 33) % (uint)(i + 1));
                    (arr[i], arr[j]) = (arr[j], arr[i]);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return arr;
    }
}
