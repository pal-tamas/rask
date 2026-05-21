using System.Reflection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared.Layout;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Layout;

// ShowcaseLayout contains an Outlet() that requires a live Router context, so the
// rendering tests drive it through App (which mounts the Router). The IsActive logic
// is unit-tested directly via reflection — no render needed.
public sealed class ShowcaseLayoutTests
{
    [Fact]
    public void RenderThroughApp_EmitsNavbarAndAside()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("navbar navbar-dark bg-dark", html);
        Assert.Contains("navbar-brand", html);
        Assert.Contains("hamburger-btn", html);
        Assert.Contains("side-nav", html);
    }

    [Fact]
    public void RenderThroughApp_GroupsLinks_UnderGroupHeaders()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        // BuildGroups emits an H6 per group label before the buttons in that group.
        Assert.Contains(">Start<", html);
        Assert.Contains(">DSL<", html);
        Assert.Contains(">Components<", html);
        Assert.Contains(">Forms<", html);
    }

    [Fact]
    public void RenderThroughApp_RootPath_MarksAtLeastOneNavItemActive()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("nav-item-btn-active", html);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("", true)]
    [InlineData("/tags", false)]
    public void IsActive_RootHref_TrueOnlyForRootPaths(string path, bool expected)
    {
        var routeState = new RouteState { Path = path };
        var layout = new ShowcaseLayout(new Navigator(routeState), routeState);
        Assert.Equal(expected, InvokePrivateIsActive(layout, "/"));
    }

    [Theory]
    [InlineData("/tags", "/tags", true)]
    [InlineData("/tags/", "/tags", true)]      // trailing slash trimmed
    [InlineData("/TAGS", "/tags", true)]       // case-insensitive
    [InlineData("/binding", "/tags", false)]
    public void IsActive_NonRootHref_MatchesPathIgnoringCaseAndTrailingSlash(string path, string href, bool expected)
    {
        var routeState = new RouteState { Path = path };
        var layout = new ShowcaseLayout(new Navigator(routeState), routeState);
        Assert.Equal(expected, InvokePrivateIsActive(layout, href));
    }

    [Fact]
    public void BypassRenderCache_Default_NotBypassed()
    {
        // The layout no longer bypasses the render cache — it subscribes to
        // RouteState.Changed instead, so it only re-renders when the route changes
        // (not on every keystroke in a child form).
        var routeState = new RouteState { Path = "/" };
        var layout = new ShowcaseLayout(new Navigator(routeState), routeState);
        var prop = typeof(Component).GetProperty("BypassRenderCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(prop);
        Assert.False((bool)prop!.GetValue(layout)!);
    }

    [Fact]
    public void OnMount_SubscribesToRouteChanged_ActiveLinkRefreshesOnNav()
    {
        // Behavioural test: navigate from "/" to "/tags", then re-render App with the
        // same RouteState. The layout's active-link computation must reflect the new
        // path — which requires its Render() to re-execute after the path change.
        // With BypassRenderCache=true the layout always re-rendered (working but
        // wasteful); with RouteState.Changed subscription it re-renders just on nav.
        var routeState = new RouteState { Path = "/" };
        var app = new Rask.Example.Shared.App();
        var services = TestServices.Default(routeState: routeState);

        var htmlAtRoot = app.RenderAsLiveRoot(services);
        // The "Welcome" link to "/" is the active one when the path is "/".
        Assert.Matches("nav-item-btn-active[^>]*>[^<]*<i class=\"bi bi-house",
            CollapseWhitespace(htmlAtRoot));

        routeState.Path = "/tags";
        var htmlAtTags = app.RenderAsLiveRoot(services);
        // After nav, the active link should be the Tags one (bi-code-slash icon).
        Assert.Matches("nav-item-btn-active[^>]*>[^<]*<i class=\"bi bi-code-slash",
            CollapseWhitespace(htmlAtTags));
    }

    private static string CollapseWhitespace(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");

    private static bool InvokePrivateIsActive(ShowcaseLayout layout, string href)
    {
        var mi = typeof(ShowcaseLayout).GetMethod("IsActive",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        return (bool)mi!.Invoke(layout, [href])!;
    }
}
