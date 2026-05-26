using System.Buffers;
using System.Globalization;
using System.Text;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks.VsBlazor.Reports;

/// <summary>
///     Diagnostic dump for the keyed-list reorder scenario. The headline metric
///     shows Rask losing 0.62× to Blazor on this case; this report breaks down
///     where the bytes go so the backlog can prioritise candidate fixes.
///     <para>
///         Invoke: <c>dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- keyed-list-dump</c>
///     </para>
/// </summary>
internal static class KeyedListDiffDump
{
    public static int Run(string[] args)
    {
        DumpScenario("KeyedList100Reorder", BuildKeyedListReorder);
        Console.WriteLine();
        DumpScenario("DeleteMiddleRow", BuildDeleteMiddleRow);
        Console.WriteLine();
        DumpScenario("KeyedListLargeAppend", BuildKeyedListLargeAppend);
        return 0;
    }

    private static (Component Before, Component After) BuildKeyedListLargeAppend()
    {
        var baseOrder = new int[100];
        for (var i = 0; i < baseOrder.Length; i++)
        {
            baseOrder[i] = i;
        }

        var largeOrder = new int[150];
        for (var i = 0; i < largeOrder.Length; i++)
        {
            largeOrder[i] = i;
        }

        return (KeyedList.BuildRask(baseOrder), KeyedList.BuildRask(largeOrder));
    }

    private static (Component Before, Component After) BuildKeyedListReorder()
    {
        var orderBefore = new int[100];
        for (var i = 0; i < orderBefore.Length; i++)
        {
            orderBefore[i] = i;
        }

        var orderAfter = (int[])orderBefore.Clone();
        (orderAfter[5], orderAfter[95]) = (orderAfter[95], orderAfter[5]);

        return (KeyedList.BuildRask(orderBefore), KeyedList.BuildRask(orderAfter));
    }

    private static (Component Before, Component After) BuildDeleteMiddleRow()
    {
        var fullOrder = new int[100];
        for (var i = 0; i < fullOrder.Length; i++)
        {
            fullOrder[i] = i;
        }

        var missingMiddle = new int[fullOrder.Length - 1];
        Array.Copy(fullOrder, 0, missingMiddle, 0, 50);
        Array.Copy(fullOrder, 51, missingMiddle, 50, fullOrder.Length - 51);

        return (AppendDeleteRowChurn.BuildRask(fullOrder), AppendDeleteRowChurn.BuildRask(missingMiddle));
    }

    private static void DumpScenario(string name, Func<(Component Before, Component After)> build)
    {
        var (treeBefore, treeAfter) = build();

        // Drive the same SessionRenderCache + HtmlSerializer pipeline LiveSession uses
        // so the diagnostic captures exactly what production would ship.
        using var cache = new SessionRenderCache();
        var htmlBefore = new StringBuilder(16 * 1024);
        var htmlAfter = new StringBuilder(16 * 1024);

        var writer = cache.PrepareCurrentBuffer();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(treeBefore, htmlBefore);
        }

        cache.Snapshot();

        writer = cache.PrepareCurrentBuffer();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(treeAfter, htmlAfter);
        }

        var ops = new List<EditOp>(128);
        var diffed = cache.TryComputeDiff(ops, htmlAfter.ToString());
        Console.WriteLine($"# {name} diff dump (TryComputeDiff returned {diffed})");
        Console.WriteLine();

        // Sample the first 10 ops to keep output readable on large diffs.
        Console.WriteLine("Idx,Kind,PathLen,PathPreview,Name,Value.Length,Length,EncodedBytes");
        var totalBytes = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var encoded = MeasureSingleOpBytes(op);
            totalBytes += encoded;
            if (i < 10 || i >= ops.Count - 2)
            {
                var pathPreview = string.Join('.', op.Path);
                var nameDisplay = op.Name ?? "";
                var valueLen = op.Value?.Length ?? 0;
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    i, op.Kind, op.Path.Length, pathPreview, nameDisplay, valueLen, op.Length, encoded));
            }
            else if (i == 10)
            {
                Console.WriteLine($"... ({ops.Count - 12} ops elided) ...");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"# Ops by kind ({ops.Count} total):");
        foreach (var group in ops.GroupBy(o => o.Kind).OrderBy(g => g.Key))
        {
            var groupBytes = group.Sum(MeasureSingleOpBytes);
            Console.WriteLine($"#   {group.Key}: {group.Count()} op(s), {groupBytes} bytes");
        }

        var fullPayload = new ArrayBufferWriter<byte>(8 * 1024);
        LivePayload.BuildPayloadUtf8Diff(fullPayload, ops, null, false);
        var envelope = fullPayload.WrittenCount - totalBytes;
        Console.WriteLine();
        Console.WriteLine($"# Full diff payload: {fullPayload.WrittenCount} bytes");
        Console.WriteLine($"# Sum of per-op (single-op) bytes: {totalBytes}");
        Console.WriteLine($"# Envelope overhead (kind/ops wrapper, comma separators): {envelope}");
    }

    private static int MeasureSingleOpBytes(EditOp op)
    {
        // Encode this op as if it were the only one in the payload; the difference
        // from `wholePayloadBytes` then attributes the JSON-envelope overhead
        // explicitly. Cheap and deterministic.
        var buf = new ArrayBufferWriter<byte>(256);
        LivePayload.BuildPayloadUtf8Diff(buf, new[] { op }, null, false);
        return buf.WrittenCount;
    }
}
