using System.Globalization;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Reports;

/// <summary>
///     Deterministic one-shot report — no BDN measurement noise. Emits a CSV row per
///     paired scenario with the bytes-on-wire each framework would ship for one
///     incremental update. Mirrors <c>Rask.Benchmarks/PayloadBytesReport.cs</c> but
///     adds the Blazor column.
///     <para>
///         Invoke: <c>dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- payload-bytes</c>
///     </para>
/// </summary>
internal static class VsBlazorPayloadBytesReport
{
    public static int Run(string[] args)
    {
        Console.WriteLine("Scenario,RaskFullBytes,RaskDiffBytes,BlazorBatchBytes,RaskDiffVsRaskFull,RaskDiffVsBlazor");

        ReportCounterOnLargePage();
        ReportTextNodeUpdate();
        ReportKeyedListReorder();

        return 0;
    }

    private static void ReportCounterOnLargePage()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(LargePageWithCounter.BuildRask(0));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(LargePageWithCounter.BuildRask(1));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(LargePageWithCounter.BuildRask(1));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = 0
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = 1
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithCounter>(before, after);

        EmitRow("CounterOnLargePage", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportTextNodeUpdate()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(LargePageWithCounter.BuildRaskWithDeepTextCell(0));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(LargePageWithCounter.BuildRaskWithDeepTextCell(1));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(LargePageWithCounter.BuildRaskWithDeepTextCell(1));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithDeepTextCell.Counter)] = 0
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithDeepTextCell.Counter)] = 1
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithDeepTextCell>(before, after);

        EmitRow("TextNodeUpdate", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportKeyedListReorder()
    {
        var orderBefore = new int[100];
        for (var i = 0; i < orderBefore.Length; i++)
        {
            orderBefore[i] = i;
        }

        var orderAfter = (int[])orderBefore.Clone();
        (orderAfter[5], orderAfter[95]) = (orderAfter[95], orderAfter[5]);

        using var rask = new RaskHarness();
        rask.SeedPrevious(KeyedList.BuildRask(orderBefore));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(KeyedList.BuildRask(orderAfter));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(KeyedList.BuildRask(orderAfter));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = orderBefore
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = orderAfter
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);

        EmitRow("KeyedList100Reorder", raskFull, raskDiff, blazorBytes);
    }

    private static void EmitRow(string name, long raskFull, long raskDiff, long blazorBytes)
    {
        var raskReduction = raskDiff > 0 ? raskFull / (double)raskDiff : 0;
        var vsBlazor = raskDiff > 0 ? blazorBytes / (double)raskDiff : 0;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4:F1}x,{5:F2}x",
            name, raskFull, raskDiff, blazorBytes, raskReduction, vsBlazor));
    }
}
