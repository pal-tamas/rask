using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK014 // a benchmark owns the root it re-renders; there is no parent to build it

namespace Rask.Benchmarks;

// A two-level route chain (layout -> list page) re-rendered as a live root, steady state.
//
// This exists to price ONE decision: Router and Outlet opt out of the render cache
// (Component.BypassRenderCache), so both re-execute on every frame instead of being served from
// cache when their props and state are clean. They have to. Router.Render() publishes ctx.Route,
// which is per-FRAME state nothing else produces — a cached Router leaves the frame with no route
// context and any Outlet that does render throws — and Outlet.Render() advances the frame-global
// RouteRenderState.Cursor, which is only coherent if every participant in the walk runs every frame.
// See #682.
//
// The claim being checked is that this is cheap: Router.Render() is a route match, Outlet.Render()
// is a dictionary lookup plus a property bind, and the PAGE components the chain resolves to are
// still cached normally — the expensive half of a routed frame is untouched. "It is only a route
// match" is a claim; this is the measurement, and it is the shape every routed Rask app renders.
[MemoryDiagnoser]
public partial class RoutedRenderBenchmarks
{
    private const int Rows = 20;

    private static readonly IReadOnlyList<Route> _routes =
    [
        new Route(typeof(LayoutPage), "/app", [new Route(typeof(ListPage), "list")])
    ];

    private RouterHost _host = null!;
    private IServiceProvider _services = null!;

    [GlobalSetup]
    public void Setup()
    {
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        _services = services.BuildServiceProvider();
        state.Path = "/app/list";

        _host = new RouterHost();
        // Render once: the measured runs are steady-state re-renders, which is where a cache
        // decision shows up at all. A first render has nothing to serve from cache.
        _host.RenderAsLiveRoot(_services);
    }

    [Benchmark]
    public string RoutedFrame() => _host.RenderAsLiveRoot(_services);

    internal sealed partial class RouterHost : Component
    {
        protected override Component? Render() => Router.Routes(_routes);
    }

    internal sealed partial class LayoutPage : Component
    {
        protected override Component? Render() => Div.Class("layout")[Outlet];
    }

    internal sealed partial class ListPage : Component
    {
        protected override Component? Render()
        {
            var rows = new List<Component>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                rows.Add(Div.Class("line").Id($"r{i}").Key(i)[Span.Class("label")[$"Item {i}"]]);
            }

            return Div.Class("wrap")[rows];
        }
    }
}
