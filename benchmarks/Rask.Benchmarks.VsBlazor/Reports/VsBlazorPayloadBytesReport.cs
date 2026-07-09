using System.Globalization;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Benchmarks;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using Rask.Core;
using Generated = Rask.Core.Components.Generated;

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
    private const string Header =
        "Scenario,RaskFullBytes,RaskDiffBytes,BlazorBatchBytes,RaskDiffVsRaskFull,RaskDiffVsBlazor";

    private readonly record struct Row(string Scenario, long RaskFull, long RaskDiff, long BlazorBytes);

    private static readonly List<Row> _rows = new();

    public static int Run(string[] args)
    {
        var check = Array.Exists(args, a => a == "--check");
        _rows.Clear();

        Console.WriteLine(Header);

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

        // Scale sweeps — one representative point per scenario.
        ReportScaleKeyedReorderLarge();
        ReportScaleKeyedRandomPermutation();
        ReportScaleKeyedAppendMiddle();
        ReportScaleDeepTreeByDepth();

        // Realistic patterns — one wire-bytes transition per scenario.
        ReportRealisticDashboardTick();
        ReportRealisticTableSortFlip();
        ReportRealisticFormFieldChurn();
        ReportRealisticNavSwitch();

        return check ? CheckAgainstBaseline() : 0;
    }

    // CI regression gate. Fails when (a) any scenario's Rask diff bytes grew vs the committed baseline,
    // or (b) any scenario stopped being a Rask win (Blazor batch <= Rask diff). Blazor's own bytes are
    // deterministic per referenced Blazor version, so they are the fixed comparison target, not gated.
    private static int CheckAgainstBaseline()
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Baselines", "vs-blazor-payload-bytes.csv");
        if (!File.Exists(baselinePath))
        {
            Console.Error.WriteLine($"::error::vs-Blazor baseline not found at {baselinePath}");
            return 1;
        }

        var baseline = ParseBaseline(baselinePath);
        var failed = false;
        Console.WriteLine();
        Console.WriteLine("Regression check vs Baselines/vs-blazor-payload-bytes.csv:");
        foreach (var row in _rows)
        {
            // Hard gate 1 — Rask must beat Blazor on every scenario. Rask ships fewer wire bytes than
            // Blazor on all of them today; this keeps it that way, so a change that lets Blazor draw
            // level or ahead on any row fails the PR. (A Blazor package bump that shifts its bytes shows
            // up here too — that's a real "we no longer win" signal worth surfacing, not noise.)
            if (row.RaskDiff > 0 && row.BlazorBytes <= row.RaskDiff)
            {
                Console.Error.WriteLine(
                    $"::error::{row.Scenario}: Blazor now ships <= Rask ({row.BlazorBytes} vs {row.RaskDiff}) — Rask no longer wins this row.");
                failed = true;
            }

            // Hard gate 2 — Rask diff bytes must not regress vs the committed baseline.
            if (baseline.TryGetValue(row.Scenario, out var b))
            {
                if (row.RaskDiff > b.RaskDiff)
                {
                    Console.Error.WriteLine(
                        $"::error::{row.Scenario}: Rask diff bytes regressed {b.RaskDiff} -> {row.RaskDiff}.");
                    failed = true;
                }
            }
            else
            {
                Console.Error.WriteLine(
                    $"::error::{row.Scenario} missing from the baseline — regenerate vs-blazor-payload-bytes.csv.");
                failed = true;
            }
        }

        if (!failed)
        {
            Console.WriteLine(
                $"  OK — all {_rows.Count} scenarios beat Blazor and hold or beat their baseline diff bytes.");
        }

        return failed ? 1 : 0;
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
                long.Parse(cells[1], CultureInfo.InvariantCulture),
                long.Parse(cells[2], CultureInfo.InvariantCulture),
                long.Parse(cells[3], CultureInfo.InvariantCulture));
        }

        return map;
    }

    private static void ReportScaleKeyedReorderLarge()
    {
        const int n = 5000;
        var before = new int[n];
        var after = new int[n];
        for (var i = 0; i < n; i++)
        {
            before[i] = i;
            after[i] = i;
        }

        (after[0], after[n - 1]) = (after[n - 1], after[0]);

        using var rask = new RaskHarness();
        rask.SeedPrevious(KeyedList.BuildRask(before));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(KeyedList.BuildRask(after));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(KeyedList.BuildRask(after));

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(KeyedList.BlazorKeyedList.Order)] = before
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(KeyedList.BlazorKeyedList.Order)] = after
            }));

        EmitRow("Scale_KeyedReorder_5000", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportScaleKeyedRandomPermutation()
    {
        const int n = 1000;
        var identity = new int[n];
        for (var i = 0; i < n; i++)
        {
            identity[i] = i;
        }

        var permuted = MicroBenchHarness.BuildLisInput(n, MicroBenchHarness.LisShape.RandomPermutation);

        using var rask = new RaskHarness();
        rask.SeedPrevious(KeyedList.BuildRask(identity));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(KeyedList.BuildRask(permuted));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(KeyedList.BuildRask(permuted));

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(KeyedList.BlazorKeyedList.Order)] = identity
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(KeyedList.BlazorKeyedList.Order)] = permuted
            }));

        EmitRow("Scale_KeyedRandomPermutation_1000", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportScaleKeyedAppendMiddle()
    {
        const int n = 2000;
        var shortArr = new int[n];
        for (var i = 0; i < n; i++)
        {
            shortArr[i] = i;
        }

        var longArr = new int[n + 1];
        for (var i = 0; i < n / 2; i++)
        {
            longArr[i] = shortArr[i];
        }

        longArr[n / 2] = n + 1000;
        for (var i = n / 2; i < n; i++)
        {
            longArr[i + 1] = shortArr[i];
        }

        using var rask = new RaskHarness();
        rask.SeedPrevious(KeyedList.BuildRask(shortArr));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(KeyedList.BuildRask(longArr));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(KeyedList.BuildRask(longArr));

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(KeyedList.BlazorKeyedList.Order)] = shortArr
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(KeyedList.BlazorKeyedList.Order)] = longArr
            }));

        EmitRow("Scale_KeyedAppendMiddle_2000", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportScaleDeepTreeByDepth()
    {
        const int depth = 200;

        Component Build(int counter)
        {
            var leaf = Generated.Span(Class: "counter")[counter.ToString()];
            for (var i = 0; i < depth; i++)
            {
                leaf = Generated.Div(Class: $"d{i}")[leaf];
            }

            return [
                Generated.Doctype(),
                Generated.Html()[
                    Generated.Body()[leaf]]];
        }

        using var rask = new RaskHarness();
        rask.SeedPrevious(Build(0));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(Build(1));
        var raskFull = rask.RenderAndBuildFullPayloadBytes(Build(1));

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes =
            blazor.MeasureIncrementalUpdate<Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree.Counter)] = 0,
                    [nameof(Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree.Depth)] = depth
                }),
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree.Counter)] = 1,
                    [nameof(Scale_DeepTreeMutationByDepthBenchmarks.ParameterizedBlazorDeepTree.Depth)] = depth
                }));

        EmitRow("Scale_DeepTreeMutationByDepth_200", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportRealisticDashboardTick()
    {
        using var rask = new RaskHarness();
#pragma warning disable RASK014
        var stateful = new DashboardWidgets.StatefulDashboard();
#pragma warning restore RASK014
        rask.SeedPrevious(stateful);
        stateful.Tick();
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(stateful);
        var raskFull = rask.RenderAndBuildFullPayloadBytes(stateful);

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<DashboardWidgets.BlazorDashboard>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DashboardWidgets.BlazorDashboard.Counter)] = 0
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DashboardWidgets.BlazorDashboard.Counter)] = 1
            }));

        EmitRow("Realistic_DashboardWidgets_Tick", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportRealisticTableSortFlip()
    {
        var initial = new int[TableSortFilter.InitialRowCount];
        var reversed = new int[TableSortFilter.InitialRowCount];
        for (var i = 0; i < TableSortFilter.InitialRowCount; i++)
        {
            initial[i] = i;
            reversed[i] = TableSortFilter.InitialRowCount - 1 - i;
        }

        using var rask = new RaskHarness();
#pragma warning disable RASK014
        var stateful = new TableSortFilter.StatefulTableSortFilter();
#pragma warning restore RASK014
        rask.SeedPrevious(stateful);
        stateful.ReverseSort();
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(stateful);
        var raskFull = rask.RenderAndBuildFullPayloadBytes(stateful);

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<TableSortFilter.BlazorTableSortFilter>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TableSortFilter.BlazorTableSortFilter.Order)] = initial
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TableSortFilter.BlazorTableSortFilter.Order)] = reversed
            }));

        EmitRow("Realistic_TableSort_Reverse", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportRealisticFormFieldChurn()
    {
        var beforeValues = new string?[FormValidationChurn.FieldCount];
        var afterValues = new string?[FormValidationChurn.FieldCount];
        var beforeInvalid = new bool[FormValidationChurn.FieldCount];
        var afterInvalid = new bool[FormValidationChurn.FieldCount];
        afterValues[0] = "v1";
        afterInvalid[0] = true;

        using var rask = new RaskHarness();
#pragma warning disable RASK014
        var stateful = new FormValidationChurn.StatefulForm();
#pragma warning restore RASK014
        rask.SeedPrevious(stateful);
        stateful.MutateField(0);
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(stateful);
        var raskFull = rask.RenderAndBuildFullPayloadBytes(stateful);

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<FormValidationChurn.BlazorForm>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(FormValidationChurn.BlazorForm.Values)] = beforeValues,
                [nameof(FormValidationChurn.BlazorForm.Invalid)] = beforeInvalid
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(FormValidationChurn.BlazorForm.Values)] = afterValues,
                [nameof(FormValidationChurn.BlazorForm.Invalid)] = afterInvalid
            }));

        EmitRow("Realistic_FormValidationChurn_Field0", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportRealisticNavSwitch()
    {
        using var rask = new RaskHarness();
#pragma warning disable RASK014
        var stateful = new NavSwitch.StatefulNavSwitch();
#pragma warning restore RASK014
        rask.SeedPrevious(stateful);
        stateful.Switch(1);
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(stateful);
        var raskFull = rask.RenderAndBuildFullPayloadBytes(stateful);

        using var blazor = new BlazorRenderBatchCapture();
        var blazorBytes = blazor.MeasureIncrementalUpdate<NavSwitch.BlazorNavSwitch>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(NavSwitch.BlazorNavSwitch.ActiveTab)] = 0
            }),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(NavSwitch.BlazorNavSwitch.ActiveTab)] = 1
            }));

        EmitRow("Realistic_NavSwitch_0to1", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportAttributeBurstUpdate()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(AttributeBurstUpdate.BuildRask(false));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(AttributeBurstUpdate.BuildRask(true));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(ThemeSwitch.BuildRask(true));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(ClassToggle.BuildRask(toIndex));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(ConditionalPanel.BuildRask(true));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(FormInputTyping.BuildRask("abcd", fieldB, fieldC));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(DeepTreeCounter.BuildRask(1));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(KeyedList.BuildRask(reverseOrder));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(NestedKeyedList.BuildRask(orderAfter));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(raskRoot);
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(LifecycleChurn.BuildRask(active));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(LifecycleChurn.BuildRask(0));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(AppendDeleteRowChurn.BuildRask(appendedOrder));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(KeyedList.BuildRask(largeOrder));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(AppendDeleteRowChurn.BuildRask(missingMiddleOrder));
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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(AttributeHeavyElements.BuildRaskMutateOne(attrCount, 1));
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
        var blazorBytes =
            blazor.MeasureIncrementalUpdate<AttributeHeavyElements.BlazorAttributeHeavyMutateOne>(before, after);

        EmitRow("AttributeUpdate", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportCounterOnLargePage()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(LargePageWithCounter.BuildRask(0));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(LargePageWithCounter.BuildRask(1));
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
        var blazorBytes =
            blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithCounter>(before, after);

        EmitRow("CounterOnLargePage", raskFull, raskDiff, blazorBytes);
    }

    private static void ReportTextNodeUpdate()
    {
        using var rask = new RaskHarness();
        rask.SeedPrevious(LargePageWithCounter.BuildRaskWithDeepTextCell(0));
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(LargePageWithCounter.BuildRaskWithDeepTextCell(1));
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
        var blazorBytes =
            blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithDeepTextCell>(before, after);

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
        var raskDiff = rask.RenderAndBuildProductionPayloadBytes(KeyedList.BuildRask(orderAfter));
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
        _rows.Add(new Row(name, raskFull, raskDiff, blazorBytes));
        var raskReduction = raskDiff > 0 ? raskFull / (double)raskDiff : 0;
        var vsBlazor = raskDiff > 0 ? blazorBytes / (double)raskDiff : 0;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4:F1}x,{5:F2}x",
            name, raskFull, raskDiff, blazorBytes, raskReduction, vsBlazor));
    }
}
