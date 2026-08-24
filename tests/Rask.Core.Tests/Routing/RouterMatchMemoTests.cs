using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// #688. Router.Render() runs on EVERY frame — it has to, because it publishes ctx.Route for the whole
// subtree — and RouteMatcher.TryMatch allocates the chain list and the values dictionary each time. The
// match is a pure function of the flattened leaves and the path, so the answer is memoised.
//
// These pin the two things a cache like that can get wrong: the parts of the render that are NOT part of
// the cache key have to stay live (the query moves without the path moving), and the key has to notice
// when the routes themselves are replaced. Both were checked by construction and are now checked by a
// test, because "it is a pure function" is a claim about code that can be edited later.
public partial class RouterMatchMemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_query_change_on_the_same_path_still_reaches_the_page()
    {
        var (view, state, sp) = BuildView([Route.To<QueryPage>("/search")]);
        state.Path = "/search";
        state.Query = new QueryCollection(new Dictionary<string, StringValues> { ["term"] = "first" });

        Assert.Equal("<span>first</span>", view.RenderAsLiveRoot(sp));

        // Same path, different query — the memoised match must be reused AND the query must not be.
        state.Query = new QueryCollection(new Dictionary<string, StringValues> { ["term"] = "second" });

        Assert.Equal("<span>second</span>", view.RenderAsLiveRoot(sp));
    }

    [Fact]
    public void Replacing_the_routes_invalidates_the_memoised_match()
    {
        // The Router is built by the chain, so the second render hands it a different Routes list. The
        // path never changes, so only the leaves-reference half of the key can catch this.
        var routes = new[] { Route.To<FirstPage>("/x") };
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();
        state.Path = "/x";

        var current = routes;
        // ReSharper disable once AccessToModifiedClosure — deliberately re-read on every render.
        var view = new StubComponent(() => Router.Routes(current));

        Assert.Equal("<i>first</i>", view.RenderAsLiveRoot(sp));

        current = [Route.To<SecondPage>("/x")];

        Assert.Equal("<b>second</b>", view.RenderAsLiveRoot(sp));
    }

    private static (StubComponent view, RouteState state, IServiceProvider sp) BuildView(
        IReadOnlyList<Route> routes)
    {
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => Router.Routes(routes));
        return (view, state, sp);
    }

    [SkipFactory]
    public sealed class QueryPage : Component
    {
        // Not named `Q`: that collides with the inherited <q> tag entry (CS0108).
        [QueryParam] public string? Term { get; set; }

        protected override Component? Render() => Span[Term ?? string.Empty];
    }

    [SkipFactory]
    public sealed class FirstPage : Component
    {
        protected override Component? Render() => I["first"];
    }

    [SkipFactory]
    public sealed class SecondPage : Component
    {
        protected override Component? Render() => B["second"];
    }
}
