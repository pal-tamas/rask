using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 2 — diff payload bytes when a parent toggles between rendering N
// user-Component children and rendering zero. The interesting axes are the
// user-Component branch in HtmlSerializer (RenderForLive cache + child slot
// reuse) and the structural-diff gate (InsertSubtree / RemoveSubtree across
// user-component subtrees).
//
// We measure the bidirectional toggle as a single iteration: alternate
// ActiveCount between 0 and MaxActiveCount on each call. The Rask side
// holds the persistent harness so the previous frame is always the prior
// iteration's render; the Blazor side feeds the two ParameterViews explicitly.

[MemoryDiagnoser]
public class LifecycleMountUnmountBenchmarks : LiveDiffPayloadBase
{
    private const int Active = LifecycleChurn.MaxActiveCount;
    private const int Inactive = 0;
    private bool _activeIsNext;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        // Seed at Inactive so the first toggle inserts 100 children.
        _activeIsNext = true;
        Rask.SeedPrevious(LifecycleChurn.BuildRask(Inactive));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_LifecycleChurn()
    {
        var (beforeCount, afterCount) = NextCounts();
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LifecycleChurn.BlazorLifecycleChurn.ActiveCount)] = beforeCount
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LifecycleChurn.BlazorLifecycleChurn.ActiveCount)] = afterCount
        });
        return Blazor.MeasureIncrementalUpdate<LifecycleChurn.BlazorLifecycleChurn>(before, after);
    }

    [Benchmark]
    public long Rask_LifecycleChurn()
    {
        var nextCount = _activeIsNext ? Active : Inactive;
        _activeIsNext = !_activeIsNext;
        return Rask.RenderAndBuildDiffPayloadBytes(LifecycleChurn.BuildRask(nextCount));
    }

    private (int Before, int After) NextCounts()
    {
        return _activeIsNext
            ? (Inactive, Active)
            : (Active, Inactive);
    }
}
