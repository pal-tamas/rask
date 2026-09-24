using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Core.Routing;
using Rask.Site;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Layout;

// ShowcaseLayout contains an Outlet() that requires a live Router context, so the
// rendering tests drive it through App (which mounts the Router). The IsActive logic
// (which drives the active-group auto-expand) is unit-tested directly via reflection.
public sealed class ShowcaseLayoutTests
{
    [Fact]
    public void Rendering_through_the_app_emits_the_navbar_sidebar_and_brand()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        // app-navbar and app-brand are hooks the E2E selects on; neither styles anything any more.
        //
        // The colour assertion is on the kit's token, not a Tailwind hue. It used to pin bg-slate-900 —
        // "what makes it the dark bar now that no framework decides that for us" — and the bar is drawn
        // from Rask.Ui's palette now, light, so a hue would only ever pin whichever one happened to be
        // chosen. bg-ui-bg says the thing that must stay true: this chrome takes its surface from the
        // shared palette rather than inventing one.
        Assert.Contains("app-navbar", html);
        Assert.Contains("bg-ui-bg", html);
        Assert.Contains("app-brand", html);
        Assert.Contains("hamburger-btn", html);

        // …and the bar is the LANDING PAGE'S, shared rather than shaped alike: a <header>, not daisyUI's
        // navbar with its two halves. SiteHeaderTests is what holds the two pages to one bar (it compares
        // them byte for byte from the wordmark rightwards); this only states that the docs route reaches
        // it. The hamburger is still daisyUI's ghost square button — the CSS that used to draw it
        // (`background: transparent; color: #fff`) existed only to survive the dark bar above it.
        Assert.Contains("<header", html);
        Assert.DoesNotContain("navbar-start", html);
        Assert.DoesNotContain("navbar-end", html);
        Assert.Contains("btn btn-ghost btn-square", html);

        // The sidebar is the kit's Ui.Sidebar: in the flow from md up and a drawer below it, with the hamburger a
        // Ui.SidebarToggle for its checkbox. It was a Bootstrap responsive offcanvas, then a hand-rolled aside.
        Assert.Contains("side-nav", html);
        Assert.Contains("md:drawer-open", html);
        Assert.Contains("for=\"docs-sidebar\"", html);
    }

    [Fact]
    public void Rendering_through_the_app_groups_links_under_group_toggles()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        // Each group renders a collapsible toggle whose label is the group name. Guides-first, so the
        // guide category groups lead (Overview + the domain groups — Data, Frontend, …); the surviving Examples group is Apps.
        Assert.Contains(">Overview<", html);
        Assert.Contains(">Data<", html);
        Assert.Contains(">Frontend<", html);
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
    public void Rendering_through_the_app_expands_the_guide_groups_and_collapses_the_example_groups()
    {
        // Guides-first: the guide category groups are expanded by default so the narrative spine is
        // visible on landing, while the demoted Examples groups stay collapsed so the ~90-item list isn't
        // dumped at once. The five guide groups (Overview + the four categories) are open.
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

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
    public void Rendering_through_the_app_at_the_root_path_marks_at_least_one_nav_link_active()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        // The kit's nav item says it is the current page to assistive tech, not only with a class.
        Assert.Matches("class=\"side-nav-link menu-active\"[^>]*aria-current=\"page\"", html);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("", true)]
    [InlineData("/tags", false)]
    public void The_root_href_is_active_only_for_root_paths(string path, bool expected)
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
    public void A_non_root_href_matches_the_path_ignoring_case_and_trailing_slash(string path, string href, bool expected)
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
    public void An_href_with_a_match_prefix_is_active_for_any_path_under_the_prefix(
        string path, string href, string? matchPrefix, bool expected)
    {
        var routeState = new RouteState { Path = path };
        var layout = new ShowcaseLayout(routeState, []);

        Assert.Equal(expected, InvokePrivateIsActive(layout, href, matchPrefix));
    }

    // (The former render-through-App "Live ticker stays active on /realtime/ETH" test is gone with the
    // Live ticker sidebar entry — its /realtime page folded into the Lifecycle guide. The MatchPrefix
    // active-link logic it exercised stays covered by An_href_with_a_match_prefix_is_active_for_any_path_under_the_prefix.)

    [Fact]
    public void The_layout_does_not_bypass_the_render_cache_by_default()
    {
        // The layout no longer bypasses the render cache — it subscribes to
        // RouteState.Changed instead, so it only re-renders when the route changes
        // (not on every keystroke in a child form).
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var layout = new ShowcaseLayout(routeState, []);

        var prop = typeof(Component).GetProperty("BypassRenderCache",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.False((bool)prop!.GetValue(layout)!);
    }

    [Fact]
    public async Task A_route_change_expands_the_active_group_and_closes_the_drawer()
    {
        // ShowcaseLayout subscribes to RouteState.Changed in Mount so that on every nav it closes the
        // mobile drawer and expands the accordion group holding the newly-active route (OnRouteChanged →
        // _drawerOpen = false + OpenActiveGroup + StateHasChanged). Those two effects are the subscription's
        // real job — NOT the sidebar's active CSS class, which each NavLink owns and refreshes off its own
        // RouteState.Changed subscription. This test asserts the two effects, so deleting the layout's
        // subscription (which leaves the drawer open and the group collapsed) turns it red.
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var services = TestServices.Default(routeState: routeState);
        // One handle across frames: the same App/layout instance re-renders after the path change.
        var page = Page.Render(new global::Rask.Site.App(), services);

        // The "Apps" accordion (Examples section, holding Todos) is collapsed at "/" — only the guide
        // groups auto-open (OpenGuideGroups). Its toggle carries the "open" class only when expanded.
        var appsExpanded = GroupExpanded("Apps");

        Assert.DoesNotMatch(appsExpanded, CollapseWhitespace(page.Html));

        // Open the mobile drawer the way a tap on the hamburger does: the hamburger is a label for the sidebar's
        // checkbox, whose change handler mirrors the state into _drawerOpen — and the checkbox renders checked.
        var opened = await page.On("#docs-sidebar").ChangeAsync("true");

        Assert.Matches("<input[^>]*id=\"docs-sidebar\"[^>]*checked|<input[^>]*checked[^>]*id=\"docs-sidebar\"", opened);

        // Navigate to /todos → RouteState.Changed fires → OnRouteChanged closes the drawer and expands the
        // group holding /todos. Without the subscription neither happens (the drawer stays open, Apps stays
        // collapsed) even though the layout still re-renders.
        routeState.Path = Rask.Site.Features.Routes.TodosPage();

        var atTodos = CollapseWhitespace(page.Render());
        Assert.Matches(appsExpanded, atTodos);                 // active group auto-expanded
        Assert.DoesNotMatch("<input[^>]*id=\"docs-sidebar\"[^>]*checked|<input[^>]*checked[^>]*id=\"docs-sidebar\"", atTodos); // drawer closed
    }

    /// <summary>
    ///     Matches the group toggle for <paramref name="group" /> only while it carries the "open" class.
    ///     The label has to sit INSIDE that toggle button: the tempered token cannot cross the button's own
    ///     closing tag, so a match means real containment.
    ///     <para>
    ///         The tempering is the point, and the reason this is not a distance window. A plain
    ///         "[\s\S]{0,N}" — and equally a lazy "svg ... /svg" — backtracks FORWARD across element
    ///         boundaries, so it cheerfully pairs one group's open toggle with a different group's label
    ///         further down the rail, and whether it does depends on how big the chevron's markup happens
    ///         to be. That made the old assertion silently size-dependent: it passed while the chevron was
    ///         a one-character glyph and broke the moment the showcase moved to real SVG icons. Widening
    ///         the window would have hidden that rather than fixed it, and would have weakened the negative
    ///         assertion, which must mean "this group is not open" and not "no open group is nearby".
    ///     </para>
    /// </summary>
    private static Regex GroupExpanded(string group) =>
        new("<button class=\"[^\"]*nav-group-toggle open\\b[^\"]*\"(?:(?!</button>)[\\s\\S])*?"
            + $"<span class=\"nav-group-label\">{Regex.Escape(group)}</span>");

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
