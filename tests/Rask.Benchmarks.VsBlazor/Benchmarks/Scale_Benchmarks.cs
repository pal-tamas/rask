using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Benchmarks;

// Scale_* — large-input sweeps. Validates that the render hot path stays linear and
// the diff codec stays sub-linear on representative-but-bigger workloads. The existing
// suite tops out around 200 rows; these classes push to 10,000 to expose any super-
// linear constants in either framework. One class per scenario per BDN's single-baseline
// constraint.

[MemoryDiagnoser]
public class Scale_StaticListLargeBenchmarks
{
    private HtmlRenderer _blazor = null!;

    [Params(1000, 5000, 10000)] public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        _blazor = new HtmlRenderer(services, NullLoggerFactory.Instance);
    }

    [GlobalCleanup]
    public void Cleanup() => _blazor.Dispose();

    [Benchmark(Baseline = true)]
    public string Blazor_Render_LargeStaticList()
    {
        return _blazor.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StaticList.BlazorStaticList.RowCount)] = RowCount
            });
            var root = await _blazor.RenderComponentAsync<StaticList.BlazorStaticList>(parameters);
            return root.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    [Benchmark]
    public string Rask_Render_LargeStaticList() => StaticList.BuildRask(RowCount).ToHtml();
}

public abstract class ScaleDiffBase
{
    protected BlazorRenderBatchCapture Blazor = null!;
    protected RaskHarness Rask = null!;

    [GlobalCleanup]
    public void Cleanup()
    {
        Rask.Dispose();
        Blazor.Dispose();
    }
}

[MemoryDiagnoser]
public class Scale_KeyedReorderLargeBenchmarks : ScaleDiffBase
{
    private int[] _blazorOrder = null!;

    private KeyedList.StatefulKeyedList _stateful = null!;
    private int _swapA;
    private int _swapB;

    [Params(1000, 5000)] public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();

#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList { InitialRowCount = N };
#pragma warning restore RASK014

        _blazorOrder = (int[])_stateful.CurrentOrder.Clone();
        _swapA = 0;
        _swapB = N - 1;
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedReorder_Large()
    {
        var beforeOrder = (int[])_blazorOrder.Clone();
        (_blazorOrder[_swapA], _blazorOrder[_swapB]) = (_blazorOrder[_swapB], _blazorOrder[_swapA]);

        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = beforeOrder
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = (int[])_blazorOrder.Clone()
        });
        var bytes = Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);
        // Step swap indices AFTER both renders observed the mutation.
        _swapA = (_swapA + 1) % N;
        _swapB = (_swapB - 1 + N) % N;
        return bytes;
    }

    [Benchmark]
    public long Rask_KeyedReorder_Large()
    {
        _stateful.SwapAt(_swapA, _swapB);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class Scale_KeyedRandomPermutationBenchmarks : ScaleDiffBase
{
    private int[] _identity = null!;
    private int[] _permuted = null!;

    private KeyedList.StatefulKeyedList _stateful = null!;
    private bool _useIdentity = true;

    [Params(100, 500, 1000)] public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _identity = new int[N];
        for (var i = 0; i < N; i++)
        {
            _identity[i] = i;
        }

        _permuted = MicroBenchHarness.BuildLisInput(N, MicroBenchHarness.LisShape.RandomPermutation);
#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList { InitialRowCount = N };
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedRandomPermutation()
    {
        var (beforeArr, afterArr) = _useIdentity ? (_identity, _permuted) : (_permuted, _identity);
        _useIdentity = !_useIdentity;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = beforeArr
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = afterArr
        });
        return Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);
    }

    [Benchmark]
    public long Rask_KeyedRandomPermutation()
    {
        var arr = _useIdentity ? _permuted : _identity;
        _useIdentity = !_useIdentity;
        _stateful.SetOrder(arr);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[MemoryDiagnoser]
public class Scale_KeyedAppendMiddleBenchmarks : ScaleDiffBase
{
    private int[] _long = null!;
    private int[] _short = null!;

    private KeyedList.StatefulKeyedList _stateful = null!;
    private bool _useShort = true;

    [Params(100, 500, 2000)] public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        _short = new int[N];
        for (var i = 0; i < N; i++)
        {
            _short[i] = i;
        }

        _long = new int[N + 1];
        var inserted = N + 1000; // unique key not present in _short
        for (var i = 0; i < N / 2; i++)
        {
            _long[i] = _short[i];
        }

        _long[N / 2] = inserted;
        for (var i = N / 2; i < N; i++)
        {
            _long[i + 1] = _short[i];
        }
#pragma warning disable RASK014
        _stateful = new KeyedList.StatefulKeyedList { InitialRowCount = N };
#pragma warning restore RASK014
        Rask.SeedPrevious(_stateful);
    }

    [Benchmark(Baseline = true)]
    public long Blazor_KeyedAppendMiddle()
    {
        var (beforeArr, afterArr) = _useShort ? (_short, _long) : (_long, _short);
        _useShort = !_useShort;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = beforeArr
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(KeyedList.BlazorKeyedList.Order)] = afterArr
        });
        return Blazor.MeasureIncrementalUpdate<KeyedList.BlazorKeyedList>(before, after);
    }

    [Benchmark]
    public long Rask_KeyedAppendMiddle()
    {
        var arr = _useShort ? _long : _short;
        _useShort = !_useShort;
        _stateful.SetOrder(arr);
        return Rask.RenderAndBuildDiffPayloadBytes(_stateful);
    }
}

[global::Rask.Core.RaskMarkup]

[MemoryDiagnoser]
public partial class Scale_DeepTreeMutationByDepthBenchmarks : ScaleDiffBase
{
    private int _counter;

    [Params(10, 50, 100, 200)] public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Rask = new RaskHarness();
        Blazor = new BlazorRenderBatchCapture();
        Rask.SeedPrevious(BuildDeepTreeRask(0, Depth));
    }

    [Benchmark(Baseline = true)]
    public long Blazor_DeepTreeMutation()
    {
        _counter++;
        var before = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ParameterizedBlazorDeepTree.Counter)] = _counter - 1,
            [nameof(ParameterizedBlazorDeepTree.Depth)] = Depth
        });
        var after = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ParameterizedBlazorDeepTree.Counter)] = _counter,
            [nameof(ParameterizedBlazorDeepTree.Depth)] = Depth
        });
        return Blazor.MeasureIncrementalUpdate<ParameterizedBlazorDeepTree>(before, after);
    }

    [Benchmark]
    public long Rask_DeepTreeMutation()
    {
        _counter++;
        return Rask.RenderAndBuildDiffPayloadBytes(BuildDeepTreeRask(_counter, Depth));
    }

    private static Component BuildDeepTreeRask(int counter, int depth)
    {
        var leaf = Span.Class("counter")[counter.ToString()];
        for (var i = 0; i < depth; i++)
        {
            leaf = Div.Class($"d{i}")[leaf];
        }

        return [Doctype, Html[Body[leaf]]];
    }

    // DeepTreeCounter.BlazorDeepTreeCounter has Depth fixed at 50. Scale_* needs a
    // parameter-driven depth so the BDN [Params] sweep can vary it; the helper lives
    // here so the scenario file owns its own knobs without bleeding into the existing
    // fixed-depth component.
    public sealed class ParameterizedBlazorDeepTree : ComponentBase
    {
        [Parameter] public int Counter { get; set; }
        [Parameter] public int Depth { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            for (var i = 0; i < Depth; i++)
            {
                b.OpenElement(0, "div");
                b.AddAttribute(1, "class", $"d{i}");
            }

            b.OpenElement(2, "span");
            b.AddAttribute(3, "class", "counter");
            b.AddContent(4, Counter.ToString());
            b.CloseElement();
            for (var i = 0; i < Depth; i++)
            {
                b.CloseElement();
            }
        }
    }
}
