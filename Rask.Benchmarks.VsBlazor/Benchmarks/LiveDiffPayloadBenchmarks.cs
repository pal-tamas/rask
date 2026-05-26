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
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        Rask.SeedPrevious(LargePageWithCounter.BuildRaskWithDeepTextCell(0));
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
        _counter++;
        return Rask.RenderAndBuildDiffPayloadBytes(LargePageWithCounter.BuildRaskWithDeepTextCell(_counter));
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
    private int[] _order = null!;
    private int _swapA;
    private int _swapB;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

        _order = new int[100];
        for (var i = 0; i < _order.Length; i++)
        {
            _order[i] = i;
        }

        _swapA = 5;
        _swapB = 95;
        Rask.SeedPrevious(KeyedList.BuildRask(_order));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedList100Reorder()
    {
        var beforeOrder = (int[])_order.Clone();
        (_order[_swapA], _order[_swapB]) = (_order[_swapB], _order[_swapA]);
        _swapA = (_swapA + 1) % _order.Length;
        _swapB = (_swapB + 1) % _order.Length;

        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = beforeOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = (int[])_order.Clone()
        });
        return Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);
    }

    [Benchmark]
    public long Rask_KeyedList100Reorder()
    {
        (_order[_swapA], _order[_swapB]) = (_order[_swapB], _order[_swapA]);
        _swapA = (_swapA + 1) % _order.Length;
        _swapB = (_swapB + 1) % _order.Length;
        return Rask.RenderAndBuildDiffPayloadBytes(KeyedList.BuildRask(_order));
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_AppendRowBenchmarks : LiveDiffPayloadBase
{
    // Toggle between InitialRowCount and InitialRowCount+1 every iteration so the
    // "previous" state always has the canonical 100 rows and the "next" state has 101.
    // Pre-built order arrays in [GlobalSetup] keep the iteration body allocation-free.
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

        _appendedIsNext = true;
        Rask.SeedPrevious(AppendDeleteRowChurn.BuildRask(_baseOrder));
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
        return Rask.RenderAndBuildDiffPayloadBytes(AppendDeleteRowChurn.BuildRask(next));
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
    // Toggle between 100 rows and 99 rows (middle row removed). The previous frame
    // is reseeded after the toggle so the Rask differ always sees a one-step delete
    // or one-step insert against the prior state.
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

        _missingIsNext = true;
        Rask.SeedPrevious(AppendDeleteRowChurn.BuildRask(_fullOrder));
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
        return Rask.RenderAndBuildDiffPayloadBytes(AppendDeleteRowChurn.BuildRask(next));
    }

    private (int[] Before, int[] After) NextOrders()
    {
        return _missingIsNext
            ? (_fullOrder, _missingMiddleOrder)
            : (_missingMiddleOrder, _fullOrder);
    }
}
