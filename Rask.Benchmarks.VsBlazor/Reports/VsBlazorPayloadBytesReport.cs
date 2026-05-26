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
        ReportDeepTreeCounterUpdate();
        ReportKeyedListReorder();
        ReportKeyedListReversal();
        ReportNestedKeyedReorder();
        ReportInputTypingBurst();
        ReportClassToggle();
        ReportMultiAttributeUpdate();
        ReportAttributeBurstUpdate();
        ReportAttributeUpdate();
        ReportAppendRow();
        ReportKeyedListLargeAppend();
        ReportDeleteMiddleRow();
        ReportConditionalRenderingToggle();
        ReportLifecycleInsert100();
        ReportLifecycleRemove100();
        ReportVirtualizationScroll();

        return 0;
    }

    private static void ReportAttributeBurstUpdate()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(AttributeBurstUpdate.BuildRask(false));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(AttributeBurstUpdate.BuildRask(true));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(AttributeBurstUpdate.BuildRask(true));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeBurstUpdate.BlazorAttributeBurst.Loaded)] = false
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeBurstUpdate.BlazorAttributeBurst.Loaded)] = true
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<AttributeBurstUpdate.BlazorAttributeBurst>(before, after);

        EmitRow("AttributeBurstUpdate", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportMultiAttributeUpdate()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(ThemeSwitch.BuildRask(false));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(ThemeSwitch.BuildRask(true));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(ThemeSwitch.BuildRask(true));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ThemeSwitch.BlazorThemeSwitch.Dark)] = false
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ThemeSwitch.BlazorThemeSwitch.Dark)] = true
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<ThemeSwitch.BlazorThemeSwitch>(before, after);

        EmitRow("MultiAttributeUpdate", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportClassToggle()
    {
        const int fromIndex = 0;
        const int toIndex = 1;

        using var rask = new RaskHarness();
        rask.SeedPrevious(ClassToggle.BuildRask(fromIndex));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(ClassToggle.BuildRask(toIndex));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(ClassToggle.BuildRask(toIndex));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ClassToggle.BlazorClassToggle.ActiveIndex)] = fromIndex
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ClassToggle.BlazorClassToggle.ActiveIndex)] = toIndex
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<ClassToggle.BlazorClassToggle>(before, after);

        EmitRow("ClassToggle", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportConditionalRenderingToggle()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(ConditionalPanel.BuildRask(false));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(ConditionalPanel.BuildRask(true));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(ConditionalPanel.BuildRask(true));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ConditionalPanel.BlazorConditionalPanel.ShowPanel)] = false
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ConditionalPanel.BlazorConditionalPanel.ShowPanel)] = true
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<ConditionalPanel.BlazorConditionalPanel>(before, after);

        EmitRow("ConditionalRenderingToggle", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportInputTypingBurst()
    {
        const string fieldB = "field B initial";
        const string fieldC = "field C initial";

        using var rask = new RaskHarness();
        rask.SeedPrevious(FormInputTyping.BuildRask("abc", fieldB, fieldC));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(FormInputTyping.BuildRask("abcd", fieldB, fieldC));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(FormInputTyping.BuildRask("abcd", fieldB, fieldC));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(FormInputTyping.BlazorFormInputTyping.A)] = "abc",
            [nameof(FormInputTyping.BlazorFormInputTyping.B)] = fieldB,
            [nameof(FormInputTyping.BlazorFormInputTyping.C)] = fieldC
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(FormInputTyping.BlazorFormInputTyping.A)] = "abcd",
            [nameof(FormInputTyping.BlazorFormInputTyping.B)] = fieldB,
            [nameof(FormInputTyping.BlazorFormInputTyping.C)] = fieldC
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<FormInputTyping.BlazorFormInputTyping>(before, after);

        EmitRow("InputTypingBurst", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportDeepTreeCounterUpdate()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(DeepTreeCounter.BuildRask(0));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(DeepTreeCounter.BuildRask(1));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(DeepTreeCounter.BuildRask(1));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DeepTreeCounter.BlazorDeepTreeCounter.Counter)] = 0
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DeepTreeCounter.BlazorDeepTreeCounter.Counter)] = 1
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<DeepTreeCounter.BlazorDeepTreeCounter>(before, after);

        EmitRow("DeepTreeCounterUpdate", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportKeyedListReversal()
    {
        const int rowCount = 50;
        var forwardOrder = new int[rowCount];
        var reverseOrder = new int[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            forwardOrder[i] = i;
            reverseOrder[i] = rowCount - 1 - i;
        }

        using var rask = new RaskHarness();
        rask.SeedPrevious(KeyedList.BuildRask(forwardOrder));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(KeyedList.BuildRask(reverseOrder));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(KeyedList.BuildRask(reverseOrder));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = forwardOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = reverseOrder
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);

        EmitRow("KeyedList50Reversal", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportNestedKeyedReorder()
    {
        var orderBefore = new int[NestedKeyedList.OuterCardCount];
        for (var i = 0; i < orderBefore.Length; i++)
        {
            orderBefore[i] = i;
        }

        var orderAfter = (int[])orderBefore.Clone();
        (orderAfter[3], orderAfter[17]) = (orderAfter[17], orderAfter[3]);

        using var rask = new RaskHarness();
        rask.SeedPrevious(NestedKeyedList.BuildRask(orderBefore));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(NestedKeyedList.BuildRask(orderAfter));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(NestedKeyedList.BuildRask(orderAfter));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(NestedKeyedList.BlazorNestedKeyedList.OuterOrder)] = orderBefore
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(NestedKeyedList.BlazorNestedKeyedList.OuterOrder)] = orderAfter
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<NestedKeyedList.BlazorNestedKeyedList>(before, after);

        EmitRow("NestedKeyedReorder", raskFull, raskDiff, blazorBytes);
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

    private static void ReportKeyedListLargeAppend()
    {
        const int baseRows = 100;
        const int appendCount = 50;
        var baseOrder = new int[baseRows];
        for (var i = 0; i < baseOrder.Length; i++)
        {
            baseOrder[i] = i;
        }

        var largeOrder = new int[baseRows + appendCount];
        for (var i = 0; i < largeOrder.Length; i++)
        {
            largeOrder[i] = i;
        }

        using var rask = new RaskHarness();
        rask.SeedPrevious(KeyedList.BuildRask(baseOrder));
        var raskDiff = rask.RenderAndBuildDiffPayloadBytes(KeyedList.BuildRask(largeOrder));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(KeyedList.BuildRask(largeOrder));

        using var blazor = new BlazorRenderBatchCapture();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = baseOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = largeOrder
        });
        var blazorBytes = blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);

        EmitRow("KeyedListLargeAppend", raskFull, raskDiff, blazorBytes);
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
