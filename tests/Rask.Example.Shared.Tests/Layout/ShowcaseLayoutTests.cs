using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Core.Routing;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Layout;

// ShowcaseLayout contains an Outlet() that requires a live Router context, so the
// rendering tests drive it through App (which mounts the Router). The IsActive logic
// (which drives the active-group auto-expand) is unit-tested directly via reflection.
public sealed class ShowcaseLayoutTests
{
    [Fact]
    public void RenderThroughApp_EmitsNavbarOffcanvasAndBrand()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        // The 5.3 dark navbar uses bg-dark + data-bs-theme (not the deprecated .navbar-dark).
        Assert.Contains("navbar", html);
        Assert.Contains("bg-dark", html);
        Assert.Contains("data-bs-theme=\"dark\"", html);
        Assert.Contains("navbar-brand", html);
        Assert.Contains("hamburger-btn", html);
        // The sidebar is a responsive offcanvas (drawer below md, static above).
        Assert.Contains("offcanvas-md offcanvas-start side-nav", html);
    }

    [Fact]
    public void RenderThroughApp_GroupsLinks_UnderGroupToggles()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        // Each group renders a collapsible toggle whose label is the group name.
        Assert.Contains(">Start<", html);
        Assert.Contains(">DSL<", html);
        Assert.Contains(">Components<", html);
        Assert.Contains(">Forms<", html);
        // The top-level sections are present, guides-first: Guides leads, then the demoted Examples and
        // Bootstrap showcases.
        Assert.Contains(">Guides<", html);
        Assert.Contains(">Examples<", html);
        Assert.Contains(">Bootstrap<", html);
    }

    [Fact]
    public void RenderThroughApp_GuidesExpanded_ExampleGroupsCollapsed()
    {
        // Guides-first: the guide category groups are expanded by default so the narrative spine is
        // visible on landing, while the demoted Examples/Bootstrap groups stay collapsed so the ~90-item
        // list isn't dumped at once. The five guide groups (Overview + the four categories) are open.
        var routeState = new RouteState { Path = "/" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        var open = Regex.Matches(html, "class=\"collapse show\"").Count;
        var closed = Regex.Matches(html, "class=\"collapse\"").Count;
        Assert.True(open >= 5, $"expected the guide groups expanded by default, only {open} open");
        Assert.True(closed >= 8, $"expected the Examples/Bootstrap groups collapsed, only {closed} closed");
    }

    [Fact]
    public void RenderThroughApp_RootPath_MarksAtLeastOneNavLinkActive()
    {
        var routeState = new RouteState { Path = "/" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("side-nav-link active", html);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("", true)]
    [InlineData("/tags", false)]
    public void IsActive_RootHref_TrueOnlyForRootPaths(string path, bool expected)
    {
        var routeState = new RouteState { Path = path };
        var layout = new ShowcaseLayout(routeState, []);
        Assert.Equal(expected, InvokePrivateIsActive(layout, "/"));
    }

    [Theory]
    [InlineData("/tags", "/tags", true)]
    [InlineData("/tags/", "/tags", true)] // trailing slash trimmed
    [InlineData("/TAGS", "/tags", true)] // case-insensitive
    [InlineData("/binding", "/tags", false)]
    public void IsActive_NonRootHref_MatchesPathIgnoringCaseAndTrailingSlash(string path, string href, bool expected)
    {
        var routeState = new RouteState { Path = path };
        var layout = new ShowcaseLayout(routeState, []);
        Assert.Equal(expected, InvokePrivateIsActive(layout, href));
    }

    // Regression: the Live ticker sidebar entry hrefs "/realtime/BTC" but the
    // page also lives at /realtime/ETH and /realtime/SOL. Switching symbol from
    // inside the page must keep the entry's group auto-expanded. Same shape for
    // /users/42 — any /users/* path should match.
    [Theory]
    [InlineData("/realtime/BTC", "/realtime/BTC", "/realtime", true)]
    [InlineData("/realtime/ETH", "/realtime/BTC", "/realtime", true)]
    [InlineData("/realtime/SOL", "/realtime/BTC", "/realtime", true)]
    [InlineData("/realtime", "/realtime/BTC", "/realtime", true)]
    [InlineData("/REALTIME/eth", "/realtime/BTC", "/realtime", true)]
    [InlineData("/realtime/BTC/", "/realtime/BTC", "/realtime", true)]
    [InlineData("/users/42", "/users/42", "/users", true)]
    [InlineData("/users/99", "/users/42", "/users", true)]
    [InlineData("/realtimes/BTC", "/realtime/BTC", "/realtime", false)] // prefix must be a full segment
    [InlineData("/toast", "/realtime/BTC", "/realtime", false)]
    public void IsActive_HrefWithMatchPrefix_TrueForAnyPathUnderPrefix(
        string path, string href, string? matchPrefix, bool expected)
    {
        var routeState = new RouteState { Path = path };
        var layout = new ShowcaseLayout(routeState, []);
        Assert.Equal(expected, InvokePrivateIsActive(layout, href, matchPrefix));
    }

    [Fact]
    public void RenderThroughApp_RealtimeEthPath_KeepsLiveTickerNavActive()
    {
        // The actual bug surface: when the route is /realtime/ETH (after switching from BTC inside
        // the LiveTickerPage), the sidebar's "Live ticker" item must still render active — now via
        // NavLink's Match (the section root) with Prefix matching.
        var routeState = new RouteState { Path = "/realtime/ETH" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Matches("side-nav-link active[^>]*>[^<]*<i class=\"bi bi-graph-up-arrow",
            CollapseWhitespace(html));
    }

    [Fact]
    public void BypassRenderCache_Default_NotBypassed()
    {
        // The layout no longer bypasses the render cache — it subscribes to
        // RouteState.Changed instead, so it only re-renders when the route changes
        // (not on every keystroke in a child form).
        var routeState = new RouteState { Path = "/" };
        var layout = new ShowcaseLayout(routeState, []);
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
        var routeState = new RouteState { Path = "/" };
        var app = new Shared.App();
        var services = TestServices.Default(routeState: routeState);

        var htmlAtRoot = app.RenderAsLiveRoot(services);
        // The "Welcome" link to "/" is the active one when the path is "/".
        Assert.Matches("side-nav-link active[^>]*>[^<]*<i class=\"bi bi-house",
            CollapseWhitespace(htmlAtRoot));

        routeState.Path = "/tags";
        var htmlAtTags = app.RenderAsLiveRoot(services);
        // After nav, the active link should be the Tags one (bi-code-slash icon).
        Assert.Matches("side-nav-link active[^>]*>[^<]*<i class=\"bi bi-code-slash",
            CollapseWhitespace(htmlAtTags));
    }

    private static string CollapseWhitespace(string s) =>
        Regex.Replace(s, @"\s+", " ");

    private static bool InvokePrivateIsActive(ShowcaseLayout layout, string href, string? matchPrefix = null)
    {
        var mi = typeof(ShowcaseLayout).GetMethod("IsActive",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        return (bool)mi!.Invoke(layout, [href, matchPrefix])!;
    }
}
