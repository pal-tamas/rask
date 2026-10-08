using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Core.Routing;
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
        var routeState = new RouteState { Path = global::Rask.Site.Routes.GuidesIndexPage() };

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
        // it. The hamburger is the kit's sidebar toggle, drawn as Flux draws it.
        Assert.Contains("<header", html);
        Assert.DoesNotContain("navbar-start", html);
        Assert.DoesNotContain("navbar-end", html);
        Assert.Contains("data-ui-sidebar-toggle", html);

        // The sidebar is the kit's Ui.Sidebar — Flux's — docked from md up and sliding over the page below it, with
        // the hamburger a Ui.SidebarToggle for its checkbox.
        Assert.Contains("side-nav", html);
        Assert.Contains("data-breakpoint=\"md\"", html);
        Assert.Contains("for=\"sidebar-open\"", html);
    }

    [Fact]
    public void Rendering_through_the_app_groups_links_under_group_toggles()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Routes.GuidesIndexPage() };

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
        var routeState = new RouteState { Path = global::Rask.Site.Routes.GuidesIndexPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        // A group is the kit's sidebar group, a native <details>: one per group, and `open` on the expanded ones.
        var groups = Regex.Matches(html, "<details[^>]*nav-group[^>]*>");
        var toggles = groups.Count;
        var expanded = groups.Count(group => Regex.IsMatch(group.Value, "\\sopen[\\s>=]"));

        Assert.True(expanded >= 5, $"expected the guide groups expanded by default, only {expanded} open");
        // Most example pages are folded into guides now; the surviving Examples group(s) (e.g. Apps/Todos)
        // stay collapsed. The guides-expanded assertion above is the primary contract.
        Assert.True(toggles > expanded, $"expected some group collapsed: {toggles} toggles, {expanded} open");
    }

    [Fact]
    public void Rendering_through_the_app_at_the_root_path_marks_at_least_one_nav_link_active()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Routes.GuidesIndexPage() };

        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        // The kit's nav item says it is the current page to assistive tech, not only with a class.
        Assert.Matches("<a[^>]*side-nav-link[^>]*aria-current=\"page\"|<a[^>]*aria-current=\"page\"[^>]*side-nav-link", html);
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
        var routeState = new RouteState { Path = global::Rask.Site.Routes.GuidesIndexPage() };
        var layout = new ShowcaseLayout(routeState, []);

        var prop = typeof(Component).GetProperty("BypassRenderCache",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.False((bool)prop!.GetValue(layout)!);
    }

    [Fact]
    public void A_route_change_expands_the_active_group_and_the_drawer_asks_the_runtime_to_close_it()
    {
        // ShowcaseLayout subscribes to RouteState.Changed in Mount so that on every nav it expands the
        // accordion group holding the newly-active route (OnRouteChanged → OpenActiveGroup + StateHasChanged).
        // Closing the mobile drawer is the runtime's: the sidebar's checkbox asks for it by attribute, so it
        // closes with no round trip (the browser suite drives that).
        var routeState = new RouteState { Path = global::Rask.Site.Routes.GuidesIndexPage() };
        var services = TestServices.Default(routeState: routeState);
        // One handle across frames: the same App/layout instance re-renders after the path change.
        var page = Page.Render(new global::Rask.Site.App(), services);

        // The "Apps" accordion (Examples section, holding Todos) is collapsed at "/" — only the guide
        // groups auto-open (OpenGuideGroups).
        var appsExpanded = GroupExpanded("Apps");

        Assert.DoesNotMatch(appsExpanded, CollapseWhitespace(page.Html));
        Assert.Matches("<input[^>]*id=\"sidebar-open\"[^>]*data-rask-uncheck-on-navigate", page.Html);

        // Navigate to /todos → RouteState.Changed fires → OnRouteChanged expands the group holding /todos.
        routeState.Path = global::Rask.Site.Routes.TodosPage();

        var atTodos = CollapseWhitespace(page.Render());
        Assert.Matches(appsExpanded, atTodos);
    }

    /// <summary>
    ///     Matches the sidebar group headed <paramref name="group" /> only while its <c>&lt;details&gt;</c> is open.
    ///     The heading has to sit INSIDE that group's own summary: the tempered token cannot cross the summary's
    ///     closing tag, so a match means real containment rather than "some open group is nearby".
    /// </summary>
    private static Regex GroupExpanded(string group) =>
        new("<details(?=[^>]*\\sopen[\\s>=])[^>]*nav-group[^>]*>\\s*<summary(?:(?!</summary>)[\\s\\S])*?"
            + $"<span[^>]*>{Regex.Escape(group)}</span>");

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
