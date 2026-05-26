using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using RaskVirtualize = Rask.Core.Components.Virtualize;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 1 + Scope 2 — render hot path and live-diff payload, head-to-head.
// Rask renders 1000 items through Virtualize (10 visible at a time); Blazor's
// counterpart renders every row through a plain @for loop. See
// VirtualizationScroll.cs for the rationale on not using Blazor's own Virtualize.

[MemoryDiagnoser]
public class RenderHotPath_VirtualizedListBenchmarks : RenderHotPathBase
{
    private RaskVirtualize _raskVirt = null!;
    private Rask.Core.Component _raskRoot = null!;

    [GlobalSetup]
    public new void Setup()
    {
        base.Setup();
        (_raskRoot, _raskVirt) = VirtualizationScroll.BuildRask();
    }

    [Benchmark(Baseline = true)]
    public string Blazor_Render() => RenderBlazor<VirtualizationScroll.BlazorAllRows>(new Dictionary<string, object?>
    {
        [nameof(VirtualizationScroll.BlazorAllRows.Count)] = VirtualizationScroll.ItemCount
    });

    [Benchmark]
    public string Rask_Render()
    {
        // Stick to viewport top; this measures cold rendering cost for a single
        // visible window. The scroll-shift dimension is exercised in Scope 2.
        VirtualizationScroll.SetScrollTop(_raskVirt, 0);
        return _raskRoot.ToHtml();
    }
}

[MemoryDiagnoser]
public class LiveDiffPayload_VirtualizationScrollBenchmarks : LiveDiffPayloadBase
{
    private RaskVirtualize _raskVirt = null!;
    private Rask.Core.Component _raskRoot = null!;
    private int _scrollStep;
    private int _blazorSalt;

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        (_raskRoot, _raskVirt) = VirtualizationScroll.BuildRask();

        VirtualizationScroll.SetScrollTop(_raskVirt, 0);
        Rask.SeedPrevious(_raskRoot);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_OneRowContentChange()
    {
        _blazorSalt++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(VirtualizationScroll.BlazorAllRows.Count)] = VirtualizationScroll.ItemCount,
            [nameof(VirtualizationScroll.BlazorAllRows.Salt)] = _blazorSalt - 1
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(VirtualizationScroll.BlazorAllRows.Count)] = VirtualizationScroll.ItemCount,
            [nameof(VirtualizationScroll.BlazorAllRows.Salt)] = _blazorSalt
        });
        return Blazor.MeasureIncrementalUpdate<VirtualizationScroll.BlazorAllRows>(before, after);
    }

    [Benchmark]
    public long Rask_ScrollShiftOneItem()
    {
        _scrollStep++;
        VirtualizationScroll.SetScrollTop(_raskVirt, _scrollStep * VirtualizationScroll.ItemSizePx);
        return Rask.RenderAndBuildDiffPayloadBytes(_raskRoot);
    }
}
