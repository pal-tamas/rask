using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 4 — startup / per-component cost. With the InlineDispatcher swap on
// BlazorRenderBatchCapture, these classes now run at BDN's default job; the previous
// classes were pinned only under --job short because the queued sync-context
// dispatcher hung on iteration 2. See Infrastructure/InlineDispatcher.cs.

public abstract class StartupPinnedBase
{
    protected HtmlRenderer Blazor = null!;
    protected IComponentActivator BlazorActivator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        Blazor = new HtmlRenderer(services, NullLoggerFactory.Instance);
        BlazorActivator = new DefaultActivator();
    }

    [GlobalCleanup]
    public void Cleanup() => Blazor.Dispose();

    private sealed class DefaultActivator : IComponentActivator
    {
        public IComponent CreateInstance(Type componentType)
            => (IComponent)Activator.CreateInstance(componentType)!;
    }
}

[MemoryDiagnoser]
public class Startup_ActivateCounterPinnedBenchmarks : StartupPinnedBase
{
    [Benchmark(Baseline = true)]
    public IComponent Blazor_Activate_Counter()
        => BlazorActivator.CreateInstance(typeof(Counter.BlazorCounter));

    [Benchmark]
    public Component Rask_Activate_Counter() => Counter.BuildRask(0);
}

[MemoryDiagnoser]
public class Startup_FirstRenderCounterPinnedBenchmarks : StartupPinnedBase
{
    [Benchmark(Baseline = true)]
    public string Blazor_FirstRender_Counter()
    {
        return Blazor.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Counter.BlazorCounter.Value)] = 0
            });
            var root = await Blazor.RenderComponentAsync<Counter.BlazorCounter>(parameters);
            return root.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    [Benchmark]
    public string Rask_FirstRender_Counter() => Counter.BuildRask(0).ToHtml();
}

[MemoryDiagnoser]
public class Startup_FirstRenderLargePagePinnedBenchmarks : StartupPinnedBase
{
    // Cold render of the 200-row page — one-shot per session, no diff codec involved.
    // Compares the raw cost of walking the tree once and stringifying. Rask's
    // ToHtml() path runs through HtmlSerializer + the StringBuilder pool; Blazor's
    // HtmlRenderer goes through RenderTreeBuilder + HtmlRenderingContext.
    [Benchmark(Baseline = true)]
    public string Blazor_FirstRender_LargePage()
    {
        return Blazor.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = 0
            });
            var root = await Blazor.RenderComponentAsync<LargePageWithCounter.BlazorLargePageWithCounter>(parameters);
            return root.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    [Benchmark]
    public string Rask_FirstRender_LargePage() => LargePageWithCounter.BuildRask(0).ToHtml();
}
