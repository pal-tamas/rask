using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

// NavigatorQueryDemo is the query-mutation widget promoted out of the former NavigatorPage when the
// routing pages were folded into the guides. It reads RouteState for the live readout and mutates the
// current URL's query through the scoped Navigator. The mutation tests exercise Navigator directly
// (page-independent); the render tests mount the demo over a stub RouteState.
public sealed partial class NavigatorQueryDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_empty_query_shows_the_empty_placeholder()
    {
        var routeState = new RouteState { Path = "/guides/routing" };

        var html = new LiveHost(() => NavigatorQueryDemo, TestServices.Default(routeState: routeState))
            .RenderAsLiveRoot();

        Assert.Contains("(empty)", html);
    }

    [Fact]
    public void A_query_shows_the_built_query_string()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = "2",
            ["sort"] = "asc"
        });
        var routeState = new RouteState { Path = "/guides/routing", Query = query };

        var html = new LiveHost(() => NavigatorQueryDemo, TestServices.Default(routeState: routeState))
            .RenderAsLiveRoot();

        Assert.Contains("page=2", html);
        Assert.Contains("sort=asc", html);
    }

    [Fact]
    public void SetQuery_from_a_handler_adds_the_key_to_the_RouteState()
    {
        var routeState = new RouteState { Path = "/guides/routing" };
        var nav = new Navigator(routeState);

        TestNavigator.RunHandler(nav, () => nav.SetQuery("page", "1"));

        Assert.True(routeState.Query.ContainsKey("page"));
    }

    [Fact]
    public void RemoveQuery_from_a_handler_drops_the_key()
    {
        var initial =
            new QueryCollection(
                new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) { ["page"] = "2" });
        var routeState = new RouteState { Path = "/guides/routing", Query = initial };
        var nav = new Navigator(routeState);

        TestNavigator.RunHandler(nav, () => nav.RemoveQuery("page"));

        Assert.False(routeState.Query.ContainsKey("page"));
    }

    [Fact]
    public void ClearQuery_from_a_handler_empties_the_query()
    {
        var initial = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "1",
            ["b"] = "2"
        });
        var routeState = new RouteState { Path = "/guides/routing", Query = initial };
        var nav = new Navigator(routeState);

        TestNavigator.RunHandler(nav, () => nav.ClearQuery());

        Assert.Equal(0, routeState.Query.Count);
    }
}
