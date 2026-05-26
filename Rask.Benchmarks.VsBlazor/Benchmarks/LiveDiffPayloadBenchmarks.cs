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
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        Rask.SeedPrevious(LargePageWithCounter.BuildRask(0));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_CounterOnLargePage()
    {
        _counter++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = _counter - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = _counter
        });
        return Blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithCounter>(before, after);
    }

    [Benchmark]
    public long Rask_CounterOnLargePage()
    {
        _counter++;
        return Rask.RenderAndBuildDiffPayloadBytes(LargePageWithCounter.BuildRask(_counter));
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
