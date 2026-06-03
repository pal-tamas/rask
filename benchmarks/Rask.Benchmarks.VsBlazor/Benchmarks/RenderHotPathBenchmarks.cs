using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 1 — pure "component tree → HTML string" cost, no live state. One class per
// scenario because BDN requires exactly one [Baseline] per (Job × Params) group; we
// want Blazor as the baseline in every pairing so the Ratio column reads "Rask vs
// Blazor". The shared base class holds the HtmlRenderer setup so the BDN runner can
// reuse it across all Scope-1 classes within a single process.

public abstract class RenderHotPathBase
{
    private HtmlRenderer _blazor = null!;

    [GlobalSetup]
    public virtual void Setup()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        _blazor = new HtmlRenderer(services, NullLoggerFactory.Instance);
    }

    [GlobalCleanup]
    public void Cleanup() => _blazor.Dispose();

    protected string RenderBlazor<TComponent>(IDictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        return _blazor.Dispatcher.InvokeAsync(async () =>
        {
            var root = await _blazor.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return root.ToHtmlString();
        }).GetAwaiter().GetResult();
    }
}

[MemoryDiagnoser]
public class RenderHotPath_StaticListBenchmarks : RenderHotPathBase
{
    [Params(5, 100, 1000)] public int RowCount { get; set; }

    [Benchmark(Baseline = true)]
    public string Blazor_Render() => RenderBlazor<StaticList.BlazorStaticList>(new Dictionary<string, object?>
    {
        [nameof(StaticList.BlazorStaticList.RowCount)] = RowCount
    });

    [Benchmark]
    public string Rask_Render() => StaticList.BuildRask(RowCount).ToHtml();
}

[MemoryDiagnoser]
public class RenderHotPath_TextHeavyBenchmarks : RenderHotPathBase
{
    [Params(5, 100, 1000)] public int RowCount { get; set; }

    [Benchmark(Baseline = true)]
    public string Blazor_Render() => RenderBlazor<TextHeavy.BlazorTextHeavy>(new Dictionary<string, object?>
    {
        [nameof(TextHeavy.BlazorTextHeavy.RowCount)] = RowCount
    });

    [Benchmark]
    public string Rask_Render() => TextHeavy.BuildRask(RowCount).ToHtml();
}

[MemoryDiagnoser]
public class RenderHotPath_CounterBenchmarks : RenderHotPathBase
{
    [Benchmark(Baseline = true)]
    public string Blazor_Render() => RenderBlazor<Counter.BlazorCounter>(new Dictionary<string, object?>
    {
        [nameof(Counter.BlazorCounter.Value)] = 42
    });

    [Benchmark]
    public string Rask_Render() => Counter.BuildRask(42).ToHtml();
}

[MemoryDiagnoser]
public class RenderHotPath_NestedTreeBenchmarks : RenderHotPathBase
{
    [Benchmark(Baseline = true)]
    public string Blazor_Render_50Deep() => RenderBlazor<NestedTree.BlazorNestedTree>(new Dictionary<string, object?>
    {
        [nameof(NestedTree.BlazorNestedTree.Depth)] = 50
    });

    [Benchmark]
    public string Rask_Render_50Deep() => NestedTree.BuildRask(50).ToHtml();
}

[MemoryDiagnoser]
public class RenderHotPath_LargePageBenchmarks : RenderHotPathBase
{
    [Benchmark(Baseline = true)]
    public string Blazor_Render() => RenderBlazor<LargePageWithCounter.BlazorLargePageWithCounter>(new Dictionary<string, object?>
    {
        [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = 1
    });

    [Benchmark]
    public string Rask_Render() => LargePageWithCounter.BuildRask(1).ToHtml();
}

[MemoryDiagnoser]
public class RenderHotPath_AttributeHeavyBenchmarks : RenderHotPathBase
{
    // 20 attrs = 3 universal (class/id/style) + 17 data-*; 50 attrs = 3 + 47 data-*.
    // 100 elements per render. The 20-attr point matches the typical design-system
    // wrapper page (Tailwind + data-test-id + ARIA); the 50-attr point stress-tests
    // the encode/append loop.
    [Params(20, 50)] public int AttrCount { get; set; }

    [Benchmark(Baseline = true)]
    public string Blazor_Render() => RenderBlazor<AttributeHeavyElements.BlazorAttributeHeavy>(new Dictionary<string, object?>
    {
        [nameof(AttributeHeavyElements.BlazorAttributeHeavy.AttrCount)] = AttrCount
    });

    [Benchmark]
    public string Rask_Render() => AttributeHeavyElements.BuildRask(AttrCount).ToHtml();
}
