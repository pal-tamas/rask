using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Realistic_* — patterns Blazor devs hit in real apps. Dashboards, sortable/filterable
// tables, forms with reactive validation, nav-driven content swaps. Each class measures
// the wire-bytes cost of one realistic state transition. Uses the stateful-root pattern
// from StatefulLargePageWithCounter so the per-iteration allocation reflects the diff
// pipeline's pooled-buffer story, not tree-rebuild noise.

public abstract class RealisticDiffBase
{
    protected BlazorRenderBatchCapture Blazor = null!;
    protected RaskHarness Rask = null!;

    [GlobalCleanup]
    public void Cleanup()
    {
        Rask.Dispose();
        Blazor.Dispose();
    }
}

[MemoryDiagnoser]
public class Realistic_DashboardWidgetsBenchmarks : RealisticDiffBase
{
    private int _blazorCounter;
    private DashboardWidgets.StatefulDashboard _stateful = null!;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new DashboardWidgets.StatefulDashboard();
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_Dashboard_CounterTick()
    {
        _blazorCounter++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DashboardWidgets.BlazorDashboard.Counter)] = _blazorCounter - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DashboardWidgets.BlazorDashboard.Counter)] = _blazorCounter
        });
        return Blazor.MeasureIncrementalUpdate<DashboardWidgets.BlazorDashboard>(before, after);
    }

    [Benchmark]
    public long Rask_Dashboard_CounterTick()
    {
        _stateful.Tick();
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class Realistic_TableSortFilterBenchmarks : RealisticDiffBase
{
    private int[] _initialOrder = null!;
    private bool _reversed;
    private int[] _reversedOrder = null!;
    private TableSortFilter.StatefulTableSortFilter _stateful = null!;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new TableSortFilter.StatefulTableSortFilter();
#pragma warning restore RASK014
        _initialOrder = new int[TableSortFilter.InitialRowCount];
        for (var i = 0; i < TableSortFilter.InitialRowCount; i++)
        {
            _initialOrder[i] = i;
        }

        _reversedOrder = new int[TableSortFilter.InitialRowCount];
        for (var i = 0; i < TableSortFilter.InitialRowCount; i++)
        {
            _reversedOrder[i] = TableSortFilter.InitialRowCount - 1 - i;
        }

        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_TableSortFlip()
    {
        var (beforeArr, afterArr) = _reversed ? (_reversedOrder, _initialOrder) : (_initialOrder, _reversedOrder);
        _reversed = !_reversed;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(TableSortFilter.BlazorTableSortFilter.Order)] = beforeArr
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(TableSortFilter.BlazorTableSortFilter.Order)] = afterArr
        });
        return Blazor.MeasureIncrementalUpdate<TableSortFilter.BlazorTableSortFilter>(before, after);
    }

    [Benchmark]
    public long Rask_TableSortFlip()
    {
        _stateful.ReverseSort();
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class Realistic_FormValidationChurnBenchmarks : RealisticDiffBase
{
    private bool[] _blazorInvalid = null!;
    private string?[] _blazorValues = null!;
    private int _cursor;
    private FormValidationChurn.StatefulForm _stateful = null!;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new FormValidationChurn.StatefulForm();
#pragma warning restore RASK014
        _blazorValues = new string?[FormValidationChurn.FieldCount];
        _blazorInvalid = new bool[FormValidationChurn.FieldCount];
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_Form_FieldChurn()
    {
        var beforeValues = (string?[])_blazorValues.Clone();
        var beforeInvalid = (bool[])_blazorInvalid.Clone();
        var i = _cursor;
        _cursor = (_cursor + 1) % FormValidationChurn.FieldCount;
        _blazorValues[i] = $"v{(_blazorValues[i]?.Length ?? 0) + 1}";
        _blazorInvalid[i] = !_blazorInvalid[i];

        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(FormValidationChurn.BlazorForm.Values)] = beforeValues,
            [nameof(FormValidationChurn.BlazorForm.Invalid)] = beforeInvalid
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(FormValidationChurn.BlazorForm.Values)] = (string?[])_blazorValues.Clone(),
            [nameof(FormValidationChurn.BlazorForm.Invalid)] = (bool[])_blazorInvalid.Clone()
        });
        return Blazor.MeasureIncrementalUpdate<FormValidationChurn.BlazorForm>(before, after);
    }

    [Benchmark]
    public long Rask_Form_FieldChurn()
    {
        var i = _cursor;
        _cursor = (_cursor + 1) % FormValidationChurn.FieldCount;
        _stateful.MutateField(i);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class Realistic_NavSwitchBenchmarks : RealisticDiffBase
{
    private int _blazorActive;
    private NavSwitch.StatefulNavSwitch _stateful = null!;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new NavSwitch.StatefulNavSwitch();
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_NavSwitch()
    {
        var beforeTab = _blazorActive;
        _blazorActive = (_blazorActive + 1) % NavSwitch.TabCount;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(NavSwitch.BlazorNavSwitch.ActiveTab)] = beforeTab
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(NavSwitch.BlazorNavSwitch.ActiveTab)] = _blazorActive
        });
        return Blazor.MeasureIncrementalUpdate<NavSwitch.BlazorNavSwitch>(before, after);
    }

    [Benchmark]
    public long Rask_NavSwitch()
    {
        _stateful.Switch((_stateful.ActiveTab + 1) % NavSwitch.TabCount);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}
