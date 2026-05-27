using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 2 — THE headline metric. Bytes-on-wire per StateHasChanged. One class per
// scenario so each can hold Baseline = Blazor against Rask without BDN complaining
// about multiple baselines in the same group. All [Benchmark] methods return long
// byte counts so BDN shows wire bytes as the bench's "Mean".

public abstract class LiveDiffPayloadBase
{
    protected RaskHarness Rask = null!;
    protected BlazorRenderBatchCapture Blazor = null!;

    [GlobalCleanup]
    public void Cleanup()
    {
        Rask.Dispose();
        Blazor.Dispose();
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_CounterOnLargePageBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: 200 rows built once in Render(), only the counter cell
    // mutates across renders. Mirrors Blazor's "re-render with new parameters" path
    // where the unchanged subtree is not rebuilt. Before the switch the Rask side
    // allocated 621 KB / iter rebuilding the 200-row tree on every call; now it
    // measures the diff codec itself.
    private StatefulLargePageWithCounter _stateful = null!;
    private int _blazorCounter;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

#pragma warning disable RASK014
        _stateful = new StatefulLargePageWithCounter();
        // Confirm the cached-rows render matches the rebuild-each-time factory's
        // output. If the two diverge, the diff-codec numbers below would compare
        // different trees — the run should fail fast rather than publish a fiction.
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_CounterOnLargePage stateful",
            _stateful,
            LargePageWithCounter.BuildRask(0));
#pragma warning restore RASK014

        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_CounterOnLargePage()
    {
        _blazorCounter++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = _blazorCounter - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = _blazorCounter
        });
        return Blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithCounter>(before, after);
    }

    [Benchmark]
    public long Rask_CounterOnLargePage()
    {
        _stateful.Tick();
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_TextNodeUpdateBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: 199 of 200 rows cached, only the mutating-cell row is
    // rebuilt per Tick. Mirrors Blazor's parameter update which also only touches
    // the changed cell's frame slot.
    private StatefulLargePageWithDeepTextCell _stateful = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

#pragma warning disable RASK014
        _stateful = new StatefulLargePageWithDeepTextCell();
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_TextNodeUpdate stateful",
            _stateful,
            LargePageWithCounter.BuildRaskWithDeepTextCell(0));
#pragma warning restore RASK014

        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_TextNodeUpdate()
    {
        _counter++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithDeepTextCell.Counter)] = _counter - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithDeepTextCell.Counter)] = _counter
        });
        return Blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithDeepTextCell>(before, after);
    }

    [Benchmark]
    public long Rask_TextNodeUpdate()
    {
        _stateful.Tick();
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_AttributeUpdateBenchmarks : LiveDiffPayloadBase
{
    // 20 attrs × 100 elements; one data-* value flips per iteration. Expect Rask to
    // emit one SetAttribute op against the cached previous frame; Blazor's batch
    // contains the same single attribute write plus the string-table reference.
    private const int AttrCount = 20;
    private int _salt;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        Rask.SeedPrevious(AttributeHeavyElements.BuildRaskMutateOne(AttrCount, 0));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_AttributeUpdate()
    {
        _salt++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.AttrCount)] = AttrCount,
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.MutationSalt)] = _salt - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.AttrCount)] = AttrCount,
            [nameof(AttributeHeavyElements.BlazorAttributeHeavyMutateOne.MutationSalt)] = _salt
        });
        return Blazor.MeasureIncrementalUpdate<AttributeHeavyElements.BlazorAttributeHeavyMutateOne>(before, after);
    }

    [Benchmark]
    public long Rask_AttributeUpdate()
    {
        _salt++;
        return Rask.RenderAndBuildDiffPayloadBytes(AttributeHeavyElements.BuildRaskMutateOne(AttrCount, _salt));
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_KeyedList100ReorderBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: 100 rows wrapped as Child instances once on first render and
    // cached by key. The benchmark Tick swaps two slots of the order array and calls
    // StateHasChanged — the next render returns a reordered list pointing at the SAME
    // row instances (no fresh Div+Span allocations). Mirrors Blazor's ParameterView
    // path, which also reuses its child component instances across the parameter
    // update. Without this, the prior rebuild-per-iter Rask version was unfairly
    // allocating 100 fresh elements every call vs Blazor's 0.
    private KeyedList.StatefulKeyedList _stateful = null!;
    private int[] _blazorOrder = null!;
    private int _blazorSwapA;
    private int _blazorSwapB;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList();
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_KeyedList100Reorder stateful",
            _stateful,
            KeyedList.BuildRask(_stateful.CurrentOrder));
#pragma warning restore RASK014

        Rask.SeedPrevious(_stateful);

        _blazorOrder = (int[])_stateful.CurrentOrder.Clone();
        _blazorSwapA = 5;
        _blazorSwapB = 95;
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedList100Reorder()
    {
        var beforeOrder = (int[])_blazorOrder.Clone();
        (_blazorOrder[_blazorSwapA], _blazorOrder[_blazorSwapB]) = (_blazorOrder[_blazorSwapB], _blazorOrder[_blazorSwapA]);
        _blazorSwapA = (_blazorSwapA + 1) % _blazorOrder.Length;
        _blazorSwapB = (_blazorSwapB + 1) % _blazorOrder.Length;

        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = beforeOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = (int[])_blazorOrder.Clone()
        });
        return Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);
    }

    [Benchmark]
    public long Rask_KeyedList100Reorder()
    {
        _stateful.SwapTwo();
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_AppendRowBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: rows cached by key, only the visible-order array swaps each
    // tick. Mirrors Blazor's ParameterView-update path so the comparison measures
    // the diff codec, not Rask paying for 100+ fresh elements per call.
    private AppendDeleteRowChurn.StatefulAppendDeleteList _stateful = null!;
    private int[] _baseOrder = null!;
    private int[] _appendedOrder = null!;
    private bool _appendedIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

        _baseOrder = new int[AppendDeleteRowChurn.InitialRowCount];
        for (var i = 0; i < _baseOrder.Length; i++)
        {
            _baseOrder[i] = i;
        }

        _appendedOrder = new int[_baseOrder.Length + 1];
        Array.Copy(_baseOrder, _appendedOrder, _baseOrder.Length);
        _appendedOrder[^1] = _baseOrder.Length;

#pragma warning disable RASK014
        _stateful = new AppendDeleteRowChurn.StatefulAppendDeleteList { Capacity = _baseOrder.Length + 1 };
        _stateful.SetOrder(_baseOrder);
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_AppendRow stateful",
            _stateful,
            AppendDeleteRowChurn.BuildRask(_baseOrder));
#pragma warning restore RASK014

        _appendedIsNext = true;
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_AppendRow()
    {
        var (before, after) = NextOrders();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = after
        });
        return Blazor.MeasureIncrementalUpdate<AppendDeleteRowChurn.BlazorAppendDeleteList>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_AppendRow()
    {
        var next = _appendedIsNext ? _appendedOrder : _baseOrder;
        _appendedIsNext = !_appendedIsNext;
        _stateful.SetOrder(next);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }

    private (int[] Before, int[] After) NextOrders()
    {
        // Blazor side doesn't share the Rask cache, so we hand it whichever pair
        // mirrors the toggle direction the Rask side will take next.
        return _appendedIsNext
            ? (_baseOrder, _appendedOrder)
            : (_appendedOrder, _baseOrder);
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_DeleteMiddleRowBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: same caching pattern as AppendRow above. Order array toggles
    // between 100 and 99 entries; rows themselves are reused across the swap.
    private AppendDeleteRowChurn.StatefulAppendDeleteList _stateful = null!;
    private int[] _fullOrder = null!;
    private int[] _missingMiddleOrder = null!;
    private bool _missingIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

        _fullOrder = new int[AppendDeleteRowChurn.InitialRowCount];
        for (var i = 0; i < _fullOrder.Length; i++)
        {
            _fullOrder[i] = i;
        }

        // Remove index 50 from the order.
        _missingMiddleOrder = new int[_fullOrder.Length - 1];
        Array.Copy(_fullOrder, 0, _missingMiddleOrder, 0, 50);
        Array.Copy(_fullOrder, 51, _missingMiddleOrder, 50, _fullOrder.Length - 51);

#pragma warning disable RASK014
        _stateful = new AppendDeleteRowChurn.StatefulAppendDeleteList { Capacity = _fullOrder.Length };
        _stateful.SetOrder(_fullOrder);
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_DeleteMiddleRow stateful",
            _stateful,
            AppendDeleteRowChurn.BuildRask(_fullOrder));
#pragma warning restore RASK014

        _missingIsNext = true;
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_DeleteMiddleRow()
    {
        var (before, after) = NextOrders();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AppendDeleteRowChurn.BlazorAppendDeleteList.Order)] = after
        });
        return Blazor.MeasureIncrementalUpdate<AppendDeleteRowChurn.BlazorAppendDeleteList>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_DeleteMiddleRow()
    {
        var next = _missingIsNext ? _missingMiddleOrder : _fullOrder;
        _missingIsNext = !_missingIsNext;
        _stateful.SetOrder(next);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }

    private (int[] Before, int[] After) NextOrders()
    {
        return _missingIsNext
            ? (_fullOrder, _missingMiddleOrder)
            : (_missingMiddleOrder, _fullOrder);
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_AttributeBurstUpdateBenchmarks : LiveDiffPayloadBase
{
    // 100 rows each gain (or lose) a data-loaded attribute when one state bit flips.
    // Diff: 100 SetAttribute ops (or 100 RemoveAttribute), all carrying the same
    // attribute name. Surfaces per-op attribute-name repetition cost; sets up the
    // future per-payload attribute-name symbol table.
    private bool _loadedIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _loadedIsNext = true;
        Rask.SeedPrevious(AttributeBurstUpdate.BuildRask(false));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_AttributeBurstUpdate()
    {
        var (before, after) = NextStates();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeBurstUpdate.BlazorAttributeBurst.Loaded)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AttributeBurstUpdate.BlazorAttributeBurst.Loaded)] = after
        });
        return Blazor.MeasureIncrementalUpdate<AttributeBurstUpdate.BlazorAttributeBurst>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_AttributeBurstUpdate()
    {
        var next = _loadedIsNext;
        _loadedIsNext = !_loadedIsNext;
        return Rask.RenderAndBuildDiffPayloadBytes(AttributeBurstUpdate.BuildRask(next));
    }

    private (bool Before, bool After) NextStates()
        => _loadedIsNext ? (false, true) : (true, false);
}

[MemoryDiagnoser]
public class LiveDiffPayload_MultiAttributeUpdateBenchmarks : LiveDiffPayloadBase
{
    // Theme switch flips 5 attrs on the root element at once. Expected: 5 SetAttribute
    // ops scoped to the root; the inner card + button stay quiet. Stresses the
    // attribute diff loop's per-element pass — it must catch all 5 mismatches in one
    // walk without spilling into sibling Insert/Remove.
    private bool _darkIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _darkIsNext = true;
        Rask.SeedPrevious(ThemeSwitch.BuildRask(false));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_MultiAttributeUpdate()
    {
        var (before, after) = NextStates();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ThemeSwitch.BlazorThemeSwitch.Dark)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ThemeSwitch.BlazorThemeSwitch.Dark)] = after
        });
        return Blazor.MeasureIncrementalUpdate<ThemeSwitch.BlazorThemeSwitch>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_MultiAttributeUpdate()
    {
        var next = _darkIsNext;
        _darkIsNext = !_darkIsNext;
        return Rask.RenderAndBuildDiffPayloadBytes(ThemeSwitch.BuildRask(next));
    }

    private (bool Before, bool After) NextStates()
        => _darkIsNext ? (false, true) : (true, false);
}

[MemoryDiagnoser]
public class LiveDiffPayload_DeepTreeCounterUpdateBenchmarks : LiveDiffPayloadBase
{
    // Counter at the bottom of a 50-deep div nest. Expected diff: one UpdateText
    // op whose path is 50+ slot indices long. Surfaces the path-encoding overhead
    // — the only piece of the wire payload that grows with tree depth.
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _counter = 0;
        Rask.SeedPrevious(DeepTreeCounter.BuildRask(_counter));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_DeepTreeCounterUpdate()
    {
        var before = _counter;
        _counter++;
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DeepTreeCounter.BlazorDeepTreeCounter.Counter)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DeepTreeCounter.BlazorDeepTreeCounter.Counter)] = _counter
        });
        return Blazor.MeasureIncrementalUpdate<DeepTreeCounter.BlazorDeepTreeCounter>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_DeepTreeCounterUpdate()
    {
        _counter++;
        return Rask.RenderAndBuildDiffPayloadBytes(DeepTreeCounter.BuildRask(_counter));
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_KeyedListLargeAppendBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: 150 distinct rows cached by key, visible-order array toggles
    // between the 100-entry base and the 150-entry full. Diff codec emits 50 keyed
    // InsertSubtree ops on the toggle to the full order, then 50 RemoveSubtree on the
    // way back. Row HTML is reused across the toggle — only the order array changes.
    private const int BaseRowCount = 100;
    private const int AppendCount = 50;
    private KeyedList.StatefulKeyedList _stateful = null!;
    private int[] _baseOrder = null!;
    private int[] _largeOrder = null!;
    private bool _largeIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

        _baseOrder = new int[BaseRowCount];
        for (var i = 0; i < _baseOrder.Length; i++)
        {
            _baseOrder[i] = i;
        }

        _largeOrder = new int[BaseRowCount + AppendCount];
        for (var i = 0; i < _largeOrder.Length; i++)
        {
            _largeOrder[i] = i;
        }

#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList { InitialRowCount = BaseRowCount };
        _stateful.SetOrder(_baseOrder);
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_KeyedListLargeAppend stateful",
            _stateful,
            KeyedList.BuildRask(_baseOrder));
#pragma warning restore RASK014

        _largeIsNext = true;
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedListLargeAppend()
    {
        var (before, after) = NextOrders();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = after
        });
        return Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_KeyedListLargeAppend()
    {
        var next = _largeIsNext ? _largeOrder : _baseOrder;
        _largeIsNext = !_largeIsNext;
        _stateful.SetOrder(next);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }

    private (int[] Before, int[] After) NextOrders()
        => _largeIsNext ? (_baseOrder, _largeOrder) : (_largeOrder, _baseOrder);
}

[MemoryDiagnoser]
public class LiveDiffPayload_KeyedListReversalBenchmarks : LiveDiffPayloadBase
{
    // Stateful Rask root: 50 rows cached by key, order array toggles forward↔reverse.
    // LIS worst case — every element off the LIS, but row instances are reused so the
    // diff path measures only the move-emission cost (and the patience-sort LIS).
    private const int RowCount = 50;
    private KeyedList.StatefulKeyedList _stateful = null!;
    private int[] _forwardOrder = null!;
    private int[] _reverseOrder = null!;
    private bool _reverseIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

        _forwardOrder = new int[RowCount];
        _reverseOrder = new int[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            _forwardOrder[i] = i;
            _reverseOrder[i] = RowCount - 1 - i;
        }

#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList { InitialRowCount = RowCount };
        _stateful.SetOrder(_forwardOrder);
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_KeyedListReversal stateful",
            _stateful,
            KeyedList.BuildRask(_forwardOrder));
#pragma warning restore RASK014

        _reverseIsNext = true;
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedListReversal()
    {
        var (before, after) = NextOrders();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = after
        });
        return Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_KeyedListReversal()
    {
        var next = _reverseIsNext ? _reverseOrder : _forwardOrder;
        _reverseIsNext = !_reverseIsNext;
        _stateful.SetOrder(next);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }

    private (int[] Before, int[] After) NextOrders()
        => _reverseIsNext ? (_forwardOrder, _reverseOrder) : (_reverseOrder, _forwardOrder);
}

[MemoryDiagnoser]
public class LiveDiffPayload_ClassToggleBenchmarks : LiveDiffPayloadBase
{
    // Moving "active" highlight in a 20-item sidebar from one row to the next. Two
    // class attrs flip per iteration: the old row loses "active", the new row gains
    // it. The expected diff is two SetAttribute ops scoped to the two affected
    // <li>s; the other 18 items and their <a> children stay out of the op stream.
    private int _activeIndex;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _activeIndex = 0;
        Rask.SeedPrevious(ClassToggle.BuildRask(_activeIndex));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_ClassToggle()
    {
        var before = _activeIndex;
        _activeIndex = (_activeIndex + 1) % ClassToggle.SidebarItemCount;
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ClassToggle.BlazorClassToggle.ActiveIndex)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ClassToggle.BlazorClassToggle.ActiveIndex)] = _activeIndex
        });
        return Blazor.MeasureIncrementalUpdate<ClassToggle.BlazorClassToggle>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_ClassToggle()
    {
        _activeIndex = (_activeIndex + 1) % ClassToggle.SidebarItemCount;
        return Rask.RenderAndBuildDiffPayloadBytes(ClassToggle.BuildRask(_activeIndex));
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_ConditionalRenderingToggleBenchmarks : LiveDiffPayloadBase
{
    // Toggle a 50-row panel in/out of the tree between header and footer. Rows are
    // unkeyed, so the positional differ emits Insert/Remove with the panel's HTML
    // fragment and the EditOp.Trusted flag stays false. Production would route this
    // through the choose-smaller gate (full HTML vs raw diff bytes); the harness
    // reports the raw diff so the comparison stays honest about what the codec
    // produces before the gate trims.
    private bool _showIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _showIsNext = true;
        Rask.SeedPrevious(ConditionalPanel.BuildRask(false));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_ConditionalRenderingToggle()
    {
        var (before, after) = NextStates();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ConditionalPanel.BlazorConditionalPanel.ShowPanel)] = before
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ConditionalPanel.BlazorConditionalPanel.ShowPanel)] = after
        });
        return Blazor.MeasureIncrementalUpdate<ConditionalPanel.BlazorConditionalPanel>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_ConditionalRenderingToggle()
    {
        var next = _showIsNext;
        _showIsNext = !_showIsNext;
        return Rask.RenderAndBuildDiffPayloadBytes(ConditionalPanel.BuildRask(next));
    }

    private (bool Before, bool After) NextStates()
        => _showIsNext ? (false, true) : (true, false);
}

[MemoryDiagnoser]
public class LiveDiffPayload_InputTypingBurstBenchmarks : LiveDiffPayloadBase
{
    // One keystroke into "Field A" — A's value goes from "abc" → "abcd" and back.
    // Sibling inputs (B, C), labels, and the submit button must NOT appear in the
    // diff; only the focused input's value attribute should mutate. The diff codec
    // scopes attribute updates to the changed element, so the expected payload is
    // a single SetAttribute op regardless of form size.
    private const string FieldB = "field B initial";
    private const string FieldC = "field C initial";
    private const string ValueShort = "abc";
    private const string ValueLong = "abcd";
    private bool _longIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _longIsNext = true;
        Rask.SeedPrevious(FormInputTyping.BuildRask(ValueShort, FieldB, FieldC));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_InputTypingBurst()
    {
        var (before, after) = NextValues();
        var beforeView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(FormInputTyping.BlazorFormInputTyping.A)] = before,
            [nameof(FormInputTyping.BlazorFormInputTyping.B)] = FieldB,
            [nameof(FormInputTyping.BlazorFormInputTyping.C)] = FieldC
        });
        var afterView = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(FormInputTyping.BlazorFormInputTyping.A)] = after,
            [nameof(FormInputTyping.BlazorFormInputTyping.B)] = FieldB,
            [nameof(FormInputTyping.BlazorFormInputTyping.C)] = FieldC
        });
        return Blazor.MeasureIncrementalUpdate<FormInputTyping.BlazorFormInputTyping>(beforeView, afterView);
    }

    [Benchmark]
    public long Rask_InputTypingBurst()
    {
        var next = _longIsNext ? ValueLong : ValueShort;
        _longIsNext = !_longIsNext;
        return Rask.RenderAndBuildDiffPayloadBytes(FormInputTyping.BuildRask(next, FieldB, FieldC));
    }

    private (string Before, string After) NextValues()
        => _longIsNext ? (ValueShort, ValueLong) : (ValueLong, ValueShort);
}

[MemoryDiagnoser]
public class LiveDiffPayload_NestedKeyedReorderBenchmarks : LiveDiffPayloadBase
{
    // Outer keyed list (20 cards) × inner keyed list (5 rows each). Swap two outer
    // cards; inner content is identical post-swap. The keyed differ must (a) emit
    // two MoveSubtree ops at the outer level and (b) recurse into kept cards without
    // emitting inner ops (their child key sets and text are unchanged). Validates
    // nested keyed matching — the recursion path through DiffKeyedSiblings into a
    // keyed parent that itself contains a keyed parent.
    // Stateful Rask root: 20 cards (each with 5 inner rows) cached once by outer key.
    // Outer-order toggles per tick; rows reused across the swap. Mirrors Blazor's
    // ParameterView path so the diff codec is what's measured, not tree construction.
    private NestedKeyedList.StatefulNestedKeyedList _stateful = null!;
    private int[] _blazorOrder = null!;
    private int _blazorSwapA;
    private int _blazorSwapB;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

#pragma warning disable RASK014
        _stateful = new NestedKeyedList.StatefulNestedKeyedList();
        ParityCheck.AssertRaskTreesMatch(
            "LiveDiffPayload_NestedKeyedReorder stateful",
            _stateful,
            NestedKeyedList.BuildRask(_stateful.CurrentOrder));
#pragma warning restore RASK014

        _blazorOrder = (int[])_stateful.CurrentOrder.Clone();
        _blazorSwapA = 3;
        _blazorSwapB = 17;
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_NestedKeyedReorder()
    {
        var beforeOrder = (int[])_blazorOrder.Clone();
        (_blazorOrder[_blazorSwapA], _blazorOrder[_blazorSwapB]) = (_blazorOrder[_blazorSwapB], _blazorOrder[_blazorSwapA]);
        _blazorSwapA = (_blazorSwapA + 1) % _blazorOrder.Length;
        _blazorSwapB = (_blazorSwapB + 1) % _blazorOrder.Length;

        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(NestedKeyedList.BlazorNestedKeyedList.OuterOrder)] = beforeOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(NestedKeyedList.BlazorNestedKeyedList.OuterOrder)] = (int[])_blazorOrder.Clone()
        });
        return Blazor.MeasureIncrementalUpdate<NestedKeyedList.BlazorNestedKeyedList>(before, after);
    }

    [Benchmark]
    public long Rask_NestedKeyedReorder()
    {
        _stateful.SwapTwo();
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}
