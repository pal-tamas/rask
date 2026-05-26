using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

/// <summary>
///     Scope 3 — event dispatch → re-render → outbound payload, in-proc, no transport.
///     Both sides skip the WS/SignalR framing (Rask's WS layer and Blazor's SignalR
///     hub overhead are excluded equally so the comparison is fair to render + serialize
///     cost). The bench shape is: from a settled "before" state, simulate one event
///     handler firing that mutates a counter, then measure the payload that would be
///     shipped on the wire.
/// </summary>
[MemoryDiagnoser]
public class LiveSessionDispatchBenchmarks
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

    [Benchmark(Baseline = true), BenchmarkCategory("Dispatch_ButtonClick_Counter")]
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

    [Benchmark, BenchmarkCategory("Dispatch_ButtonClick_Counter")]
    public long Rask_Dispatch_ButtonClick_Counter()
    {
        _counter++;
        // Rask's handler dispatch in production: WS receives event, handler delegate
        // mutates state, RenderAsLiveRoot rebuilds the tree, BuildPayloadUtf8Diff ships.
        // The handler delegate itself is one virtual call — dwarfed by render cost.
        return _rask.RenderAndBuildDiffPayloadBytes(Counter.BuildRask(_counter));
    }
}
