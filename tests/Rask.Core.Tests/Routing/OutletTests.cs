using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

public class OutletTests
{
    private static (StubComponent view, RouteState state, IServiceProvider sp) BuildView(IReadOnlyList<Route> routes)
    {
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => Router(routes));
        return (view, state, sp);
    }

    [Fact]
    public void Outlet_EndOfChain_RendersEmptyFragment()
    {
        // Leaf page itself calls Outlet(); cursor is past end of chain → empty Fragment.
        var (view, state, sp) = BuildView(new[] { Route<LeafWithOutlet>("/leaf") });
        state.Path = "/leaf";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div>leaf:</div>", html);
    }

    [Fact]
    public void Outlet_ThreeLevelChain_RendersNested()
    {
        var routes = new[]
        {
            Route<Layout>("/app",
                new[]
                {
                    Route<Section>("section",
                        new[] { Route<Leaf>("leaf/{tag}") })
                })
        };
        var (view, state, sp) = BuildView(routes);
        state.Path = "/app/section/leaf/x";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div>layout:<section>section:<span>leaf:x</span></section></div>", html);
    }

    [Fact]
    public void Outlet_TwoOutletsInSameRender_SecondAdvancesCursor()
    {
        // Layout renders two consecutive Outlet() calls. First pulls Leaf, second
        // finds cursor past end → empty Fragment. Proves Cursor++ semantics.
        var routes = new[]
        {
            Route<DoubleOutletLayout>("/d",
                new[] { Route<LeafText>("leaf") })
        };
        var (view, state, sp) = BuildView(routes);
        state.Path = "/d/leaf";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div>first:<span>leaf</span>second:</div>", html);
    }

    [Fact]
    public void Outlet_ChildRenderThrows_ErrorContainedToOutlet_LayoutStaysLive()
    {
        // A child page faults during render. The default per-outlet boundary contains it to
        // the outlet region: the layout shell (nav) stays live and the DefaultErrorPage
        // renders in place, instead of the fault bubbling out and replacing everything.
        var routes = new[]
        {
            Route<LayoutWithNav>("/app", new[] { Route<ThrowingLeaf>("boom") })
        };
        var (view, state, sp) = BuildView(routes);
        state.Path = "/app/boom";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Contains("nav:", html, StringComparison.Ordinal);
        Assert.Contains("rask-error-boundary", html, StringComparison.Ordinal);
        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
        Assert.Contains("boom!", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Outlet_DisableErrorBoundaryTrue_ChildThrowPropagates()
    {
        // Opting out lets the fault bubble past the outlet (here, out of the render entirely
        // since the test root has no RootErrorBoundary) — proving the boundary is off.
        var routes = new[]
        {
            Route<LayoutNoBoundary>("/app", new[] { Route<ThrowingLeaf>("boom") })
        };
        var (view, state, sp) = BuildView(routes);
        state.Path = "/app/boom";

        var ex = Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot(sp));
        Assert.Equal("boom!", ex.Message);
    }

    [Fact]
    public void Outlet_BoundaryRecoversOnNavigation_AfterChildCrash()
    {
        // The boundary is reused positionally across renders. Without recover-on-nav it would
        // stay tripped and keep showing the crash over the next (healthy) page. Navigating away
        // must clear it.
        var routes = new[]
        {
            Route<LayoutWithNav>("/app",
                new[] { Route<ThrowingLeaf>("boom"), Route<HealthyLeaf>("ok") })
        };
        var (view, state, sp) = BuildView(routes);

        state.Path = "/app/boom";
        var crashed = view.RenderAsLiveRoot(sp);
        Assert.Contains("rask-error-boundary", crashed, StringComparison.Ordinal);

        state.Path = "/app/ok";
        var recovered = view.RenderAsLiveRoot(sp);

        Assert.DoesNotContain("rask-error-boundary", recovered, StringComparison.Ordinal);
        Assert.Contains("ok!", recovered, StringComparison.Ordinal);
    }

    [SkipFactory]
    public sealed class Layout : Component
    {
        protected override RenderResult Render() => Div()["layout:", Outlet()];
    }

    [SkipFactory]
    public sealed class HealthyLeaf : Component
    {
        protected override RenderResult Render() => Span()["ok!"];
    }

    [SkipFactory]
    public sealed class LayoutWithNav : Component
    {
        protected override RenderResult Render() => Div()["nav:", Outlet()];
    }

    [SkipFactory]
    public sealed class LayoutNoBoundary : Component
    {
        protected override RenderResult Render() => Div()["nav:", Outlet(DisableErrorBoundary: true)];
    }

    [SkipFactory]
    public sealed class ThrowingLeaf : Component
    {
        protected override RenderResult Render() => throw new InvalidOperationException("boom!");
    }

    [SkipFactory]
    public sealed class Section : Component
    {
        protected override RenderResult Render() =>
            Section()["section:", Outlet()];
    }

    [SkipFactory]
    public sealed class Leaf : Component
    {
        [RouteParam] public string? Tag { get; set; }
        protected override RenderResult Render() => Span()[$"leaf:{Tag}"];
    }

    [SkipFactory]
    public sealed class LeafWithOutlet : Component
    {
        protected override RenderResult Render() => Div()["leaf:", Outlet()];
    }

    [SkipFactory]
    public sealed class DoubleOutletLayout : Component
    {
        protected override RenderResult Render() => Div()["first:", Outlet(), "second:", Outlet()];
    }

    [SkipFactory]
    public sealed class LeafText : Component
    {
        protected override RenderResult Render() => Span()["leaf"];
    }
}
