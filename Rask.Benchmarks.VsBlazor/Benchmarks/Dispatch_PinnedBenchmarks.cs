using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 3 — event dispatch → re-render → outbound payload, in-proc, no transport.
// Both sides skip WS/SignalR framing (Rask's WS layer and Blazor's SignalR hub
// overhead are excluded equally so the comparison is fair to render + serialize cost).
// Bench shape: from a settled "before" state, simulate one event handler firing that
// mutates a counter, then measure the payload that would be shipped on the wire.
//
// Default-job pinnable since the InlineDispatcher swap on BlazorRenderBatchCapture
// (see Infrastructure/InlineDispatcher.cs) — the previous LiveSessionDispatchBenchmarks
// class was stuck on --job short because the queued sync-context dispatcher hung the
// second iteration.

[MemoryDiagnoser]
public class Dispatch_ButtonClickCounterPinnedBenchmarks
{
    private RaskHarness _rask = null!;
    private BlazorRenderBatchCapture _blazor = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        _rask = new RaskHarness();
        _blazor = new BlazorRenderBatchCapture();
        _rask.SeedPrevious(Counter.BuildRask(0));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rask.Dispose();
        _blazor.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long Blazor_Dispatch_ButtonClick_Counter()
    {
        _counter++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(Counter.BlazorCounter.Value)] = _counter - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(Counter.BlazorCounter.Value)] = _counter
        });
        return _blazor.MeasureIncrementalUpdate<Counter.BlazorCounter>(before, after);
    }

    [Benchmark]
    public long Rask_Dispatch_ButtonClick_Counter()
    {
        _counter++;
        return _rask.RenderAndBuildDiffPayloadBytes(Counter.BuildRask(_counter));
    }
}

[MemoryDiagnoser]
public class Dispatch_ButtonClickLargePagePinnedBenchmarks
{
    // Stateful 200-row root: one event handler ticks the counter cell, the rest of
    // the page (200 rows) is unchanged across renders. This is the production shape
    // where the diff codec earns its keep — one mutation deep inside a quiet tree.
    private RaskHarness _rask = null!;
    private BlazorRenderBatchCapture _blazor = null!;
    private StatefulLargePageWithCounter _stateful = null!;
    private int _blazorCounter;

    [GlobalSetup]
    public void Setup()
    {
        _rask = new RaskHarness();
        _blazor = new BlazorRenderBatchCapture();
#pragma warning disable RASK014
        _stateful = new StatefulLargePageWithCounter();
#pragma warning restore RASK014
        _rask.SeedPrevious(_stateful);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rask.Dispose();
        _blazor.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long Blazor_Dispatch_ButtonClick_LargePage()
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
        return _blazor.MeasureIncrementalUpdate<LargePageWithCounter.BlazorLargePageWithCounter>(before, after);
    }

    [Benchmark]
    public long Rask_Dispatch_ButtonClick_LargePage()
    {
        _stateful.Tick();
        return _rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}
