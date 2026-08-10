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

    [SkipFactory]
    public sealed class Layout : Component
    {
        protected override Component? Render() => Div["layout:", Outlet];
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
