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
    public void RenderThroughApp_EmitsNavbarSidebarAndBrand()
    {
        var routeState = new RouteState { Path = "/" };
        var html = RaskTest.Render(new Shared.App(), TestServices.Default(routeState: routeState)).Html;

        // app-navbar and app-brand are hooks the scoped stylesheet and the E2E both select on;
        // bg-slate-900 is what makes it the dark bar now that no framework decides that for us.
        Assert.Contains("app-navbar", html);
        Assert.Contains("bg-slate-900", html);
        Assert.Contains("app-brand", html);
        Assert.Contains("hamburger-btn", html);

        // The sidebar is in the flow from md up and a drawer below it. It was a Bootstrap responsive
        // offcanvas; the behaviour is unchanged because the open state was always Rask state.
        Assert.Contains("side-nav", html);
        Assert.Contains("md:flex", html);
    }

    [Fact]
    public void RenderThroughApp_GroupsLinks_UnderGroupToggles()
    {
        var routeState = new RouteState { Path = "/" };
        var html = RaskTest.Render(new Shared.App(), TestServices.Default(routeState: routeState)).Html;

        // Each group renders a collapsible toggle whose label is the group name. Guides-first, so the
        // guide category groups lead (Overview + Core + Bootstrap + …); the surviving Examples group is Apps.
        Assert.Contains(">Overview<", html);
        Assert.Contains(">Core<", html);
        Assert.Contains(">Apps<", html);
        // The top-level sections are present, guides-first: Guides leads, then the demoted Examples.
        Assert.Contains(">Guides<", html);
        Assert.Contains(">Examples<", html);

        // And no Bootstrap group at all: the package is gone, so a sidebar entry for it would be a link
        // to nothing. Asserted as an ABSENCE because the category list is data — an empty category
        // renders no heading, so its removal is invisible unless something looks for it.
        Assert.DoesNotContain(">Bootstrap<", html);
    }

    [Fact]
    public void RenderThroughApp_GuidesExpanded_ExampleGroupsCollapsed()
    {
        // Guides-first: the guide category groups are expanded by default so the narrative spine is
        // visible on landing, while the demoted Examples groups stay collapsed so the ~90-item list isn't
        // dumped at once. The five guide groups (Overview + the four categories) are open.
        var routeState = new RouteState { Path = "/" };
        var html = RaskTest.Render(new Shared.App(), TestServices.Default(routeState: routeState)).Html;

        // A closed group renders NO items element now, where BsCollapse rendered one with .collapse and
        // hid it — so "expanded" is the presence of the container and "collapsed" is its absence. The
        // toggle button is what is always there, one per group.
        var toggles = Regex.Matches(html, "nav-group-toggle").Count;
        var expanded = Regex.Matches(html, "nav-group-items").Count;
        Assert.True(expanded >= 5, $"expected the guide groups expanded by default, only {expanded} open");
        // Most example pages are folded into guides now; the surviving Examples group(s) (e.g. Apps/Todos)
        // stay collapsed. The guides-expanded assertion above is the primary contract.
        Assert.True(toggles > expanded, $"expected some group collapsed: {toggles} toggles, {expanded} open");
    }

    [Fact]
    public void RenderThroughApp_RootPath_MarksAtLeastOneNavLinkActive()
    {
        var routeState = new RouteState { Path = "/" };
        var html = RaskTest.Render(new Shared.App(), TestServices.Default(routeState: routeState)).Html;

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

    // (The former render-through-App "Live ticker stays active on /realtime/ETH" test is gone with the
    // Live ticker sidebar entry — its /realtime page folded into the Lifecycle guide. The MatchPrefix
    // active-link logic it exercised stays covered by IsActive_HrefWithMatchPrefix_TrueForAnyPathUnderPrefix.)

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
    public async Task OnRouteChanged_ExpandsActiveGroupAndClosesDrawer()
    {
        // ShowcaseLayout subscribes to RouteState.Changed in OnMount so that on every nav it closes the
        // mobile drawer and expands the accordion group holding the newly-active route (OnRouteChanged →
        // _drawerOpen = false + OpenActiveGroup + StateHasChanged). Those two effects are the subscription's
        // real job — NOT the sidebar's active CSS class, which each NavLink owns and refreshes off its own
        // RouteState.Changed subscription. This test asserts the two effects, so deleting the layout's
        // subscription (which leaves the drawer open and the group collapsed) turns it red.
        var routeState = new RouteState { Path = "/" };
        var services = TestServices.Default(routeState: routeState);
        // One handle across frames: the same App/layout instance re-renders after the path change.
        var page = RaskTest.Render(new Shared.App(), services);

        // The "Apps" accordion (Examples section, holding Todos) is collapsed at "/" — only the guide
        // groups auto-open (OpenGuideGroups). Its toggle carries the "open" class only when expanded.
        var appsExpanded = new Regex("nav-group-toggle open\"[\\s\\S]{0,200}nav-group-label\">Apps<");
        Assert.DoesNotMatch(appsExpanded, CollapseWhitespace(page.Html));

        // Open the mobile drawer via the hamburger (it toggles _drawerOpen); the backdrop marks it open.
        var hamburgerId = Regex.Match(page.Html, "hamburger-btn[^\"]*\"[^>]*data-rask-on-click=\"([^\"]+)\"")
            .Groups[1].Value;
        Assert.NotEqual("", hamburgerId);
        // The open drawer renders its own backdrop element; BsOffcanvas called it .offcanvas-backdrop.
        Assert.Contains("nav-backdrop", await page.InvokeAsync(hamburgerId));

        // Navigate to /todos → RouteState.Changed fires → OnRouteChanged closes the drawer and expands the
        // group holding /todos. Without the subscription neither happens (the drawer stays open, Apps stays
        // collapsed) even though the layout still re-renders.
        routeState.Path = Rask.Example.Shared.Features.Routes.TodosPage();
        var atTodos = CollapseWhitespace(page.Render());
        Assert.Matches(appsExpanded, atTodos);                 // active group auto-expanded
        Assert.DoesNotContain("nav-backdrop", atTodos);         // drawer closed
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
