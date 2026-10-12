using System.Buffers;
using System.Runtime.InteropServices;

namespace Rask.Core.Live;

// A child that comes or goes among siblings that otherwise still pair up: `saved ? Callout : null` above a
// form. The positional walk pairs by slot, so it sees every later sibling as changed and answers with ops
// the gate refuses — the whole page. When the two sides differ by ONE contiguous run of children, that run
// is the change: it ships as trusted inserts or removes, and the siblings around it are diffed as the pairs
// they are, which never touches a node that stayed (its focus, its caret, what was typed into it).
//
// Reached only after the positional walk met a tag mismatch or was left with a tail, at a nested level.
public static partial class FrameDiffer
{
    // The run is scored at no more positions than this. Past it the run stays where the positional walk
    // would put it, which is what ships today.
    private const int MaxScoredPositions = 256;

    private const string HandlerAttributePrefix = "data-rask-on-";

    private static bool TryDiffOneRun(
        Level old, Level current, List<EditOp> output, ReadOnlySpan<char> newHtml, DiffScratch scratch,
        int opCountAtEntry, bool levelHadReplace)
    {
        var paired = Math.Min(old.Count, current.Count);
        if (old.Count == current.Count || paired == 0)
        {
            return false;
        }

        if (!TryPlaceRun(old, current, out var run))
        {
            return false;
        }

        // A run at the very end of a level nothing was replaced in is the append or truncation the walk
        // already wrote, pair for pair. Its ops stand.
        if (!levelHadReplace && run.At == paired)
        {
            return false;
        }

        if (InsideForeignContent(current.Frames, scratch.Path))
        {
            return false;
        }

        output.RemoveRange(opCountAtEntry, output.Count - opCountAtEntry);
        DiffAroundRun(old, current, run, output, newHtml, scratch);
        return true;
    }

    // Ops in the order the client applies them: the pairs before the run, the run, then the pairs after it
    // at the slots they have once the run is in or out.
    private static void DiffAroundRun(
        Level old, Level current, Run run, List<EditOp> output, ReadOnlySpan<char> newHtml, DiffScratch scratch)
    {
        var oi = old.Start;
        var ni = current.Start;
        var domSlot = 0;
        while (oi < run.OldFrom)
        {
            DiffMatchedPair(old.Frames, ref oi, current.Frames, ref ni, ref domSlot, output, newHtml, scratch);
        }

        EmitTail(old.Frames, run.OldFrom, run.OldTo, current.Frames, run.NewFrom, run.NewTo, domSlot, true, output, newHtml,
            scratch.Path);
        domSlot += Math.Max(current.Count - old.Count, 0);
        oi = run.OldTo;
        ni = run.NewTo;
        while (oi < old.End && ni < current.End)
        {
            DiffMatchedPair(old.Frames, ref oi, current.Frames, ref ni, ref domSlot, output, newHtml, scratch);
        }
    }

    // The children the two sides share at the front and at the back. When those cover the shorter side, the
    // longer side's extra children are one run, somewhere between the two.
    private static bool TryPlaceRun(Level old, Level current, out Run run)
    {
        var oldAt = ArrayPool<int>.Shared.Rent(old.Count);
        var newAt = ArrayPool<int>.Shared.Rent(current.Count);
        try
        {
            FillChildIndexes(old, oldAt);
            FillChildIndexes(current, newAt);
            var paired = Math.Min(old.Count, current.Count);
            var front = 0;
            while (front < paired && Pairs(old, oldAt[front], current, newAt[front]))
            {
                front++;
            }

            var back = 0;
            while (back < paired && Pairs(old, oldAt[old.Count - 1 - back], current, newAt[current.Count - 1 - back]))
            {
                back++;
            }

            if (front + back < paired)
            {
                run = default;
                return false;
            }

            var at = ChoosePosition(old, oldAt, current, newAt, paired - back, front);
            var length = Math.Abs(current.Count - old.Count);
            var oldFrom = FrameAt(old, oldAt, at);
            var newFrom = FrameAt(current, newAt, at);
            run = current.Count > old.Count
                ? new Run(at, oldFrom, oldFrom, newFrom, FrameAt(current, newAt, at + length))
                : new Run(at, oldFrom, FrameAt(old, oldAt, at + length), newFrom, newFrom);
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(oldAt);
            ArrayPool<int>.Shared.Return(newAt);
        }
    }

    // Among same-tag siblings the run fits at every position from `first` to `last`. The last is where the
    // positional walk would put it, and it stays there unless another position leaves strictly more of the
    // siblings paired with what they already were.
    private static int ChoosePosition(Level old, int[] oldAt, Level current, int[] newAt, int first, int last)
    {
        var positions = last - first;
        if (positions is 0 or > MaxScoredPositions)
        {
            return last;
        }

        // Scored from the last position leftwards, so the walk's own position is the one to beat and a tie
        // keeps the rightmost. When every sibling is already paired with its exact self there, nothing can.
        Span<byte> beforeRun = stackalloc byte[positions];
        var score = 0;
        for (var j = first; j < last; j++)
        {
            var similarity = Similarity(old.Frames, oldAt[j], current.Frames, newAt[j]);
            beforeRun[j - first] = (byte)similarity;
            score += similarity;
        }

        if (score == 2 * positions)
        {
            return last;
        }

        var inserting = current.Count > old.Count;
        var length = Math.Abs(current.Count - old.Count);
        var best = score;
        var at = last;
        for (var j = last - 1; j >= first; j--)
        {
            var afterRun = inserting
                ? Similarity(old.Frames, oldAt[j], current.Frames, newAt[j + length])
                : Similarity(old.Frames, oldAt[j + length], current.Frames, newAt[j]);
            score += afterRun - beforeRun[j - first];
            if (score > best)
            {
                best = score;
                at = j;
            }
        }

        return at;
    }

    // 2: the same subtree, handler ids aside. 1: the same element with the same attributes of its own.
    // 0: neither. Handler ids are left out because a handler that comes or goes above renumbers the ones
    // below it, in siblings that are otherwise exactly what they were.
    private static int Similarity(ReadOnlySpan<RenderFrame> oldFrames, int oi, ReadOnlySpan<RenderFrame> newFrames, int ni)
    {
        var oldLength = oldFrames[oi].SubtreeLength;
        var newLength = newFrames[ni].SubtreeLength;
        var shared = Math.Min(oldLength, newLength);
        var same = 0;
        while (same < shared && SameFrame(oldFrames, oi + same, newFrames, ni + same))
        {
            same++;
        }

        if (same == shared && oldLength == newLength)
        {
            return 2;
        }

        if (oldFrames[oi].Kind != RenderFrameKind.Element)
        {
            return 0;
        }

        var own = 1 + CountLeadingAttributes(oldFrames, oi + 1, oi + oldLength);
        return same >= own && own == 1 + CountLeadingAttributes(newFrames, ni + 1, ni + newLength) ? 1 : 0;
    }

    private static bool SameFrame(ReadOnlySpan<RenderFrame> oldFrames, int oi, ReadOnlySpan<RenderFrame> newFrames, int ni)
    {
        ref readonly var a = ref oldFrames[oi];
        ref readonly var b = ref newFrames[ni];
        return a.Kind == b.Kind
               && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
               && (string.Equals(a.Value, b.Value, StringComparison.Ordinal)
                   || (a.Kind == RenderFrameKind.Attribute && a.Name is { } name
                       && name.StartsWith(HandlerAttributePrefix, StringComparison.Ordinal)));
    }

    // The positional walk pairs two elements on their tag alone. A pair around a run must also carry the
    // same key, or none: a key is the page saying which child this is.
    private static bool Pairs(Level old, int oi, Level current, int ni)
    {
        if (!SiblingMatches(old.Frames[oi], current.Frames[ni]))
        {
            return false;
        }

        return old.Frames[oi].Kind != RenderFrameKind.Element
               || string.Equals(ExtractRaskKey(old.Frames, oi, old.End), ExtractRaskKey(current.Frames, ni, current.End),
                   StringComparison.Ordinal);
    }

    // The client parses an inserted fragment in a <template>, where an <svg> or <math> child read on its
    // own is an unknown HTML element. Below either of those the level is left to the whole-page morph.
    private static bool InsideForeignContent(ReadOnlySpan<RenderFrame> newFrames, List<int> path)
    {
        var start = 0;
        var end = newFrames.Length;
#pragma warning disable S3267 // a span cannot be captured by the lambda a query would need
        foreach (var slot in path)
#pragma warning restore S3267
        {
            var i = start;
            var before = slot;
            while (i < end && (newFrames[i].Kind == RenderFrameKind.Attribute || before > 0))
            {
                if (newFrames[i].Kind != RenderFrameKind.Attribute)
                {
                    before--;
                }

                i += newFrames[i].SubtreeLength;
            }

            if (i >= end)
            {
                return true;
            }

            ref readonly var ancestor = ref newFrames[i];
            if (ancestor.Kind == RenderFrameKind.Element
                && (string.Equals(ancestor.Name, "svg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ancestor.Name, "math", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            start = i + 1;
            end = i + ancestor.SubtreeLength;
        }

        return false;
    }

    private static void FillChildIndexes(Level level, int[] indexes)
    {
        var count = 0;
        var i = level.Start;
        while (i < level.End)
        {
            if (level.Frames[i].Kind != RenderFrameKind.Attribute)
            {
                indexes[count++] = i;
            }

            i += level.Frames[i].SubtreeLength;
        }
    }

    private static int FrameAt(Level level, int[] indexes, int child) => child < level.Count ? indexes[child] : level.End;

    // One side of a sibling level: its frames, and how many of them are children.
    private readonly ref struct Level(ReadOnlySpan<RenderFrame> frames, int start, int end, int count)
    {
        public static Level Of(ReadOnlySpan<RenderFrame> frames, int start, int end) =>
            new(frames, start, end, DomNodeCount(frames, start, end));


        public ReadOnlySpan<RenderFrame> Frames { get; } = frames;

        public int Start { get; } = start;

        public int End { get; } = end;

        public int Count { get; } = count;
    }

    // Where the run starts among the children, and the frames it spans on each side: empty on the old side
    // for an insert, empty on the new side for a remove.
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Run(int At, int OldFrom, int OldTo, int NewFrom, int NewTo);
}
