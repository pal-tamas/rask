using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

public partial class OutletTests : global::Rask.Core.RaskMarkup
{
    private static (StubComponent view, RouteState state, IServiceProvider sp) BuildView(IReadOnlyList<Route> routes)
    {
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => Router.Routes(routes));
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
    public void Outlet_ParentRerendersWhileRouterIsClean_StillHasRouteContext()
    {
        // #682. Router publishes ctx.Route, and that is per-FRAME state: it lives only for the walk
        // that assigned it. So a frame in which Router does NOT execute has no route context at all,
        // and any Outlet that DOES execute in that frame reaches RouteChainRenderer with a null route
        // and throws — the app becomes a "Something went wrong" boundary, and the symptom (a missing
        // sidebar) names nothing.
        //
        // Getting there needs one frame where the layout re-renders and Router does not, with the
        // Outlet re-created rather than served from cache. The sibling count before the Outlet is what
        // re-creates it: child identity is the ordinal among entry-built children (#685), so growing
        // the count by one shifts the Outlet into a slot that misses its lookup.
        //
        // Before the fix this threw InvalidOperationException on the second render. It could not fire
        // before the chain surface — the generated factory re-applied every property every render, so
        // nothing was ever really render-cached and Router always re-executed.
        var routes = new[] { Route<ShiftingLayout>("/s", new[] { Route<LeafText>("leaf") }) };
        var (view, state, sp) = BuildView(routes);
        state.Path = "/s/leaf";

        ShiftingLayout.Captured = null;
        ShiftingLayout.Extra = 0;
        var first = view.RenderAsLiveRoot(sp);
        Assert.Equal("<div><span>leaf</span></div>", first);

        // Only the layout is dirty. Router's props and state are both clean, so the render cache
        // skips its Render() — and with it the ctx.Route assignment the whole subtree depends on.
        ShiftingLayout.Extra = 1;
        ShiftingLayout.Captured!.StateHasChanged();

        var second = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><i></i><span>leaf</span></div>", second);
    }

    [SkipFactory]
    public sealed class Layout : Component
    {
        protected override Component? Render() => Div["layout:", Outlet];
    }

    // Renders `Extra` entry-built siblings ahead of its Outlet, so a test can change how many
    // children precede it between frames. Captures itself because the route chain constructs its
    // pages through ActivatorUtilities — a test cannot hand one in.
    [SkipFactory]
    public sealed class ShiftingLayout : Component
    {
        public static int Extra;
        public static ShiftingLayout? Captured;

        public ShiftingLayout() => Captured = this;

        protected override Component? Render()
        {
            var kids = new List<Component>(Extra + 1);
            for (var i = 0; i < Extra; i++)
            {
                kids.Add(I);
            }

            kids.Add(Outlet);
            return Div[kids];
        }
    }

    [SkipFactory]
    // `new`: a nested component named after a tag. The generator no longer injects an entry for it (that
    // would be CS0102 against this very declaration), but the inherited <section> entry is still there to
    // hide — CS0108, and `new` is what says the nested component is the one meant here.
    public new sealed class Section : Component
    {
        protected override Component? Render() =>
            Section["section:", Outlet];
    }

    [SkipFactory]
    public sealed class Leaf : Component
    {
        [RouteParam] public string? Tag { get; set; }
        protected override Component? Render() => Span[$"leaf:{Tag}"];
    }

    [SkipFactory]
    public sealed class LeafWithOutlet : Component
    {
        protected override Component? Render() => Div["leaf:", Outlet];
    }

    [SkipFactory]
    public sealed class DoubleOutletLayout : Component
    {
        protected override Component? Render() => Div["first:", Outlet, "second:", Outlet];
    }

    [SkipFactory]
    public sealed class LeafText : Component
    {
        protected override Component? Render() => Span["leaf"];
    }
}
