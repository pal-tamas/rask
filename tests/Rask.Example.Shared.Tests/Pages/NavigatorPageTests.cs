using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class NavigatorPageTests
{
    [Fact]
    public void Render_EmptyQuery_ShowsEmptyPlaceholder()
    {
        var routeState = new RouteState { Path = "/navigator" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));
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
        var routeState = new RouteState { Path = "/navigator", Query = query };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));
        Assert.Contains("page=2", html);
        Assert.Contains("sort=asc", html);
    }

    [Fact]
    public void SetQuery_FromHandler_AddsKeyToRouteState()
    {
        var routeState = new RouteState { Path = "/navigator" };
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
        var routeState = new RouteState { Path = "/navigator", Query = initial };
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
        var routeState = new RouteState { Path = "/navigator", Query = initial };
        var nav = new Navigator(routeState);

        TestNavigator.RunHandler(nav, () => nav.ClearQuery());
        Assert.Equal(0, routeState.Query.Count);
    }
}
