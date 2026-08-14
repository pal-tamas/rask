using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// NavigatorQueryDemo is the query-mutation widget promoted out of the former NavigatorPage when the
// routing pages were folded into the guides. It reads RouteState for the live readout and mutates the
// current URL's query through the scoped Navigator. The mutation tests exercise Navigator directly
// (page-independent); the render tests mount the demo over a stub RouteState.
public sealed partial class NavigatorQueryDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_EmptyQuery_ShowsEmptyPlaceholder()
    {
        var routeState = new RouteState { Path = "/guides/routing" };
        var html = new LiveHost(() => NavigatorQueryDemo, TestServices.Default(routeState: routeState))
            .RenderAsLiveRoot();
        Assert.Contains("(empty)", html);
    }

    [Fact]
    public void Render_WithQuery_ShowsBuiltQueryString()
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
    public void SetQuery_FromHandler_AddsKeyToRouteState()
    {
        var routeState = new RouteState { Path = "/guides/routing" };
        var nav = new Navigator(routeState);
        TestNavigator.RunHandler(nav, () => nav.SetQuery("page", "1"));
        Assert.True(routeState.Query.ContainsKey("page"));
    }

    [Fact]
    public void RemoveQuery_FromHandler_DropsKey()
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
    public void ClearQuery_FromHandler_EmptiesQuery()
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
