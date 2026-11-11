using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Caching;

#pragma warning disable RASK014 // a page root is constructed, as a host constructs one

namespace Rask.Benchmarks;

// What Cached saves (#1323): a request's render of a dashboard whose aggregate is computed in its own render — TA's
// shape — drawn every time, against the same page with the dashboard behind Cached and the store warm. Each call is a
// fresh page, as a request is: a new root, so nothing is served from a component's own render cache.
[MemoryDiagnoser]
public partial class CachedFragmentBenchmarks : global::Rask.Core.RaskMarkup
{
    private IServiceProvider _services = null!;

    [Params(50, 500)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _services = new ServiceCollection()
            .AddSingleton<IFragmentStore>(new MemoryFragmentStore())
            .BuildServiceProvider();

        // Warm: the first render draws and stores it.
        _ = CachedPage().RenderAsLiveRoot(_services);
    }

    [Benchmark(Baseline = true)]
    public string Uncached() => new BenchPage(() => Div[BenchDashboard.Rows(Rows)]).RenderAsLiveRoot(_services);

    [Benchmark]
    public string CachedHit() => CachedPage().RenderAsLiveRoot(_services);

    private BenchPage CachedPage() => new(() => Div[Cached.Name("dashboard").VaryBy(Rows)[BenchDashboard.Rows(Rows)]]);
}

/// <summary>A page root rendering what it is handed.</summary>
public sealed partial class BenchPage(Func<Component> page) : Component
{
    protected override Component? Render() => page();
}

/// <summary>An aggregate over its rows, grouped and totalled, drawn as a table — the render a cache saves.</summary>
public sealed partial class BenchDashboard : Component
{
    /// <summary>How many orders it aggregates.</summary>
    public int Rows { get; set; }

    protected override Component? Render()
    {
        var groups = Enumerable.Range(0, Rows)
            .Select(i => (Region: "Region " + (i % 12), Total: (i * 37 % 1000) / 10m))
            .GroupBy(o => o.Region)
            .Select(g => (Region: g.Key, Count: g.Count(), Sum: g.Sum(o => o.Total)))
            .OrderByDescending(g => g.Sum)
            .ToList();

        return Table.Class("dashboard")[
            Thead[Tr[Th["Region"], Th["Orders"], Th["Total"]]],
            Tbody[groups.Select(g => Tr.Key(g.Region)[Td[g.Region], Td[g.Count], Td[g.Sum]])],
            Tfoot[Enumerable.Range(0, Rows).Select(i => Tr.Key(i)[Td[$"Order {i}"], Td[i % 12], Td[(i * 37 % 1000) / 10m]])]
        ];
    }
}
