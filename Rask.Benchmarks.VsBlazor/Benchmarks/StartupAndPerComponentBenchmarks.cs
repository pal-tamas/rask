using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scope 4 — startup / per-component cost. Split per scenario for the single-baseline
// constraint.

public abstract class StartupAndPerComponentBase
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
public class StartupAndPerComponent_ActivateBenchmarks : StartupAndPerComponentBase
{
    [Benchmark(Baseline = true)]
    public IComponent Blazor_Activate_Counter()
        => BlazorActivator.CreateInstance(typeof(Counter.BlazorCounter));

    [Benchmark]
    public Component Rask_Activate_Counter() => Counter.BuildRask(0);
}

[MemoryDiagnoser]
public class StartupAndPerComponent_FirstRenderBenchmarks : StartupAndPerComponentBase
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
