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
        ReportAttributeUpdate();
        ReportAppendRow();
        ReportDeleteMiddleRow();
        ReportLifecycleInsert100();
        ReportLifecycleRemove100();
        ReportVirtualizationScroll();

        return 0;
    }

    private static void ReportVirtualizationScroll()
    {
        var (raskRoot, raskVirt) = VirtualizationScroll.BuildRask();
        VirtualizationScroll.SetScrollTop(raskVirt, 0);

        using var rask = new RaskHarness();
        rask.SeedPrevious(raskRoot);

        VirtualizationScroll.SetScrollTop(raskVirt, VirtualizationScroll.ItemSizePx);
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(raskRoot);
        var raskFull = rask.RenderAndBuildFullPayloadBytes(raskRoot);

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(VirtualizationScroll.BlazorAllRows.Count)] = VirtualizationScroll.ItemCount,
            [nameof(VirtualizationScroll.BlazorAllRows.Salt)] = 0
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(VirtualizationScroll.BlazorAllRows.Count)] = VirtualizationScroll.ItemCount,
            [nameof(VirtualizationScroll.BlazorAllRows.Salt)] = 1
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<VirtualizationScroll.BlazorAllRows>(before, after);

        EmitRow("VirtualizationScroll", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportLifecycleInsert100()
    {
        const int active = LifecycleChurn.MaxActiveCount;

        using var rask = new RaskHarness();
        rask.SeedPrevious(LifecycleChurn.BuildRask(0));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(LifecycleChurn.BuildRask(active));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(LifecycleChurn.BuildRask(active));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LifecycleChurn.BlazorLifecycleChurn.ActiveCount)] = 0
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LifecycleChurn.BlazorLifecycleChurn.ActiveCount)] = active
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<LifecycleChurn.BlazorLifecycleChurn>(before, after);

        EmitRow("Lifecycle_Insert100", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportLifecycleRemove100()
    {
        const int active = LifecycleChurn.MaxActiveCount;

        using var rask = new RaskHarness();
        rask.SeedPrevious(LifecycleChurn.BuildRask(active));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(LifecycleChurn.BuildRask(0));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(LifecycleChurn.BuildRask(0));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LifecycleChurn.BlazorLifecycleChurn.ActiveCount)] = active
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LifecycleChurn.BlazorLifecycleChurn.ActiveCount)] = 0
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<LifecycleChurn.BlazorLifecycleChurn>(before, after);

        EmitRow("Lifecycle_Remove100", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportAppendRow()
    {
        var baseOrder = new int[AppendDeleteRowChurn.InitialRowCount];
        for (var i = 0; i < baseOrder.Length; i++)
        {
            baseOrder[i] = i;
        }

        var appendedOrder = new int[baseOrder.Length + 1];
        Array.Copy(baseOrder, appendedOrder, baseOrder.Length);
        appendedOrder[^1] = baseOrder.Length;

        using var rask = new RaskHarness();
        rask.SeedPrevious(AppendDeleteRowChurn.BuildRask(baseOrder));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(AppendDeleteRowChurn.BuildRask(appendedOrder));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(AppendDeleteRowChurn.BuildRask(appendedOrder));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = baseOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = appendedOrder
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<AppendDeleteRowChurn.BlazorAppendDeleteList>(before, after);

        EmitRow("AppendRow", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportDeleteMiddleRow()
    {
        var fullOrder = new int[AppendDeleteRowChurn.InitialRowCount];
        for (var i = 0; i < fullOrder.Length; i++)
        {
            fullOrder[i] = i;
        }

        var missingMiddleOrder = new int[fullOrder.Length - 1];
        Array.Copy(fullOrder, 0, missingMiddleOrder, 0, 50);
        Array.Copy(fullOrder, 51, missingMiddleOrder, 50, fullOrder.Length - 51);

        using var rask = new RaskHarness();
        rask.SeedPrevious(AppendDeleteRowChurn.BuildRask(fullOrder));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(AppendDeleteRowChurn.BuildRask(missingMiddleOrder));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(AppendDeleteRowChurn.BuildRask(missingMiddleOrder));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = fullOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = missingMiddleOrder
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<AppendDeleteRowChurn.BlazorAppendDeleteList>(before, after);

        EmitRow("DeleteMiddleRow", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportAttributeUpdate()
    {
        const int attrCount = 20;
        using var rask = new RaskHarness();
        rask.SeedPrevious(AttributeHeavyElements.BuildRaskMutateOne(attrCount, 0));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(AttributeHeavyElements.BuildRaskMutateOne(attrCount, 1));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(AttributeHeavyElements.BuildRaskMutateOne(attrCount, 1));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.AttrCount)] = attrCount,
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.MutationSalt)] = 0
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.AttrCount)] = attrCount,
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.MutationSalt)] = 1
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<AttributeHeavyElements.BlazorAttributeHeavyMutateOne>(before, after);

        EmitRow("AttributeUpdate", raskFull, raskDiff, blazorBytes);
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
