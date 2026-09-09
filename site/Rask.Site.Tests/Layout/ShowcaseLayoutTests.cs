using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Core.Routing;
using Rask.Site;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;
using Rask.Ui;

using Entry = (string Path, string Label, Rask.Ui.UiIconName Icon, string Group, string? MatchPrefix);

namespace Rask.Site.Tests.Layout;

// ShowcaseLayout contains an Outlet() that requires a live Router context, so the
// rendering tests drive it through App (which mounts the Router). The IsActive logic
// (which drives the active-group auto-expand) is unit-tested directly via reflection.
public sealed class ShowcaseLayoutTests
{
    [Fact]
    public void RenderThroughApp_EmitsNavbarSidebarAndBrand()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        // app-navbar and app-brand are hooks the scoped stylesheet and the E2E both select on.
        //
        // The colour assertion is on a THEME token, not a Tailwind hue. It pinned bg-slate-900 once
        // ("what makes it the dark bar"), then bg-ui-bg when the bar moved to the kit's palette; it is
        // bg-base-100 now that the bar follows the reader's chosen theme. The thing that must stay true
        // is the same throughout: this chrome takes its surface from the shared palette rather than
        // inventing one. ui-* still resolves to exactly this token — the name changed, not the colour.
        Assert.Contains("app-navbar", html);
        Assert.Contains("bg-base-100", html);
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
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

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
    public void GroupByName_MergesAGroupThatAppearsTwice()
    {
        // Regression: the Examples section drew "PWA" TWICE.
        //
        // The sidebar grouped by consecutive RUN, and the entries arrive in DI registration order.
        // Program.cs registers PWA, then Islands, then six UI kit, then twelve more PWA - two runs of
        // the same name, so two group blocks: the heading appeared twice, both derived the same
        // GroupKey so one chevron opened and closed both, and two sibling <li> carried the same Key,
        // which RASK022 holds a keyed list to as identity.
        //
        // Exercised on the grouping function with Program.cs's actual shape rather than through a
        // render: the entries come from DI, the test host registers none, so a rendered sidebar has no
        // Examples groups at all and could never show the defect. That is exactly how the first version
        // of this test passed while proving nothing.
        var links = new List<Entry>
        {
            ("/pwa", "PWA demo", UiIconName.Phone, "PWA", null),
            ("/islands", "Islands", UiIconName.Overview, "Islands", null),
            ("/ui/actions", "Actions", UiIconName.Check, "UI kit", null),
            ("/ui/layout", "Layout", UiIconName.Desktop, "UI kit", null),
            ("/install-prompt", "Install prompt", UiIconName.Download, "PWA", null),
            ("/wake-lock", "Wake lock", UiIconName.Desktop, "PWA", null),
        };

        var grouped = InvokeGroupByName(links).ToList();

        Assert.Equal(new[] { "PWA", "Islands", "UI kit" }, grouped.Select(g => g.Group).ToArray());

        // Merged, not merely deduplicated: every PWA entry has to end up in the one block.
        var pwa = grouped.Single(g => g.Group == "PWA").Items.ToList();
        Assert.Equal(3, pwa.Count);
        Assert.Contains(pwa, e => e.Label == "PWA demo");
        Assert.Contains(pwa, e => e.Label == "Wake lock");
    }

    [Fact]
    public void GroupByName_KeepsFirstAppearanceOrderForAlreadyConsecutiveInput()
    {
        // The guide catalog is authored in order and its groups are already consecutive, so this has to
        // behave exactly as the old run-based grouping did for it.
        var links = new List<Entry>
        {
            ("/a", "A", UiIconName.Book, "Overview", null),
            ("/b", "B", UiIconName.Book, "Overview", null),
            ("/c", "C", UiIconName.Cube, "Core", null),
        };

        var grouped = InvokeGroupByName(links).ToList();

        Assert.Equal(new[] { "Overview", "Core" }, grouped.Select(g => g.Group).ToArray());
        Assert.Equal(2, grouped[0].Items.Count);
    }

    [Fact]
    public void RenderThroughApp_RendersNoSidebarGroupTwice()
    {
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        var labels = Regex.Matches(html, "nav-group-label\"[^>]*>([^<]+)<")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(labels);
        var duplicated = labels.GroupBy(l => l, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + " x" + g.Count())
            .ToList();

        Assert.True(duplicated.Count == 0, "a sidebar group is rendered more than once: " + string.Join(", ", duplicated));
    }

    [Fact]
    public void RenderThroughApp_GuidesExpanded_ExampleGroupsCollapsed()
    {
        // Guides-first: the guide category groups are expanded by default so the narrative spine is
        // visible on landing, while the demoted Examples groups stay collapsed so the ~90-item list isn't
        // dumped at once. The five guide groups (Overview + the four categories) are open.
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

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
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

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
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
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
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };
        var services = TestServices.Default(routeState: routeState);
        // One handle across frames: the same App/layout instance re-renders after the path change.
        var page = RaskTest.Render(new global::Rask.Site.App(), services);

        // The "Apps" accordion (Examples section, holding Todos) is collapsed at "/" — only the guide
        // groups auto-open (OpenGuideGroups). Its toggle carries the "open" class only when expanded.
        var appsExpanded = GroupExpanded("Apps");
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
        routeState.Path = Rask.Site.Features.Routes.TodosPage();
        var atTodos = CollapseWhitespace(page.Render());
        Assert.Matches(appsExpanded, atTodos);                 // active group auto-expanded
        Assert.DoesNotContain("nav-backdrop", atTodos);         // drawer closed
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

    private static IEnumerable<(string Group, List<Entry> Items)> InvokeGroupByName(
        IEnumerable<Entry> links)
    {
        var mi = typeof(ShowcaseLayout).GetMethod("GroupByName",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        return (IEnumerable<(string Group, List<Entry> Items)>)mi!.Invoke(null, new object[] { links })!;
    }

    private static bool InvokePrivateIsActive(ShowcaseLayout layout, string href, string? matchPrefix = null)
    {
        var mi = typeof(ShowcaseLayout).GetMethod("IsActive",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        return (bool)mi!.Invoke(layout, [href, matchPrefix])!;
    }
}
