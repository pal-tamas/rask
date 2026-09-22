namespace Rask.Ui.Tests.Components;

/// <summary>
///     The sidebar's two independent collapses, and the slots that hold their place.
/// </summary>
/// <remarks>
///     <para>
///     <c>Collapsible</c> and <c>Collapsable</c> are different questions and both are real: the first says at what
///     width the sidebar stops being beside the page at all, and the second keeps it beside the page and takes the
///     words away. An application can want either, or both.
///     </para>
///     <para>
///     Both live in a checkbox, so they work on a prerendered page with no runtime — which is the reason to pin
///     the markup rather than the behaviour: what is being asserted is that the state has somewhere to live that
///     needs no script.
///     </para>
/// </remarks>
public partial class UiSidebarRailTests : global::Rask.Core.RaskMarkup
{
    private static string Sidebar(bool collapsable) =>
        (collapsable
            ? UiSidebar.Id("nav").Page(Div["page"]).Collapsible(UiBreakpoint.Lg).Collapsable(true)
            : UiSidebar.Id("nav").Page(Div["page"]).Collapsible(UiBreakpoint.Lg))[
            UiSidebarHeader[UiBrand.Label("Rask").Href("/")],
            UiNavList[UiNavItem.Label("Overview").Href("/")],
            UiSidebarFooter[UiProfile.Name("Ada Lovelace")]
        ].ToHtml();

    [Fact]
    public void Everything_the_rail_strips_to_an_icon_keeps_its_name_as_a_title()
    {
        // #1119: collapsed to the rail, a nav link is an icon and nothing else. Its title is the tooltip the rail
        // shows, and the accessible name the link falls back to once its words are display:none — a CSS tooltip
        // would be clipped by the panel, which hides its overflow.
        var html = Sidebar(collapsable: true);

        Assert.Matches("<a [^>]*title=\"Overview\"", html);
        Assert.Matches("<a [^>]*title=\"Rask\"", html);
        Assert.Contains("title=\"Ada Lovelace\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sidebar_that_cannot_collapse_carries_no_rail_checkbox()
    {
        // A stray input nobody can reach is a tab stop nobody asked for, and an id another page element could
        // collide with.
        var html = Sidebar(collapsable: false);

        Assert.DoesNotContain("ui-sidebar-rail", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-rail", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_it_adds_the_checkbox_and_says_which_breakpoint_it_applies_from()
    {
        var html = Sidebar(collapsable: true);

        Assert.Contains("id=\"nav-rail\"", html, StringComparison.Ordinal);
        Assert.Contains("ui-sidebar-rail", html, StringComparison.Ordinal);
        // The breakpoint travels as a VALUE: the rail applies only while the sidebar is docked, and no Tailwind
        // variant can say "while this drawer is open in the flow".
        Assert.Contains("data-ui-rail=\"lg\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_collapse_control_points_at_the_rail_checkbox_rather_than_the_drawers()
    {
        // Two checkboxes, two labels: the toggle opens the drawer on a phone, this narrows the docked sidebar.
        // Aiming this one at the drawer's id would slide the sidebar away instead of narrowing it.
        var html = UiSidebarCollapse.For("nav").Collapsible(UiBreakpoint.Lg).ToHtml();

        Assert.Contains("for=\"nav-rail\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"button\"", html, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Collapse sidebar\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_collapse_control_appears_where_the_toggle_disappears()
    {
        // They are opposites: the toggle is for a sidebar that slides over the page, this one for a sidebar
        // that has docked. Showing both at once would offer two controls for two different collapses.
        Assert.Contains("lg:hidden",
            UiSidebarToggle.For("nav").Collapsible(UiBreakpoint.Lg).ToHtml(), StringComparison.Ordinal);

        var collapse = UiSidebarCollapse.For("nav").Collapsible(UiBreakpoint.Lg).ToHtml();
        Assert.Contains("hidden", collapse, StringComparison.Ordinal);
        Assert.Contains("lg:inline-flex", collapse, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapsed_states_the_rail_and_leaves_it_to_the_checkbox_when_unset()
    {
        Assert.Contains("checked",
            UiSidebar.Id("nav").Page(Div["page"]).Collapsable(true).Collapsed(true)[Div].ToHtml(),
            StringComparison.Ordinal);

        // Uncontrolled: nothing is checked, and the reader owns it — which is what lets it work with no runtime
        // at all.
        Assert.DoesNotContain("checked",
            UiSidebar.Id("nav").Page(Div["page"]).Collapsable(true)[Div].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnCollapse_is_what_lets_a_page_remember_the_choice()
    {
        // The state lives in a checkbox, which a full page load forgets. A page that wants it remembered takes
        // ownership, and this is how it hears the reader change it — driven through the real event rather than
        // asserted on an attribute, because a static render carries no handler attributes at all.
        bool? heard = null;
        var page = global::Rask.Testing.Test.Render(
            UiSidebar.Id("nav").Page(Div["page"]).Collapsable(true).OnCollapse(v => heard = v)[Div]);

        await page.On("#nav-rail").ChangeAsync("true");

        Assert.True(heard);
    }

    [Fact]
    public void The_footer_holds_its_place_while_the_nav_scrolls()
    {
        // mt-auto is what pins it without a UiSpacer in front of it, and the hairline is what separates it from
        // a nav list long enough to run into it.
        var html = UiSidebarFooter[Div["x"]].ToHtml();

        Assert.Contains("mt-auto", html, StringComparison.Ordinal);
        Assert.Contains("border-t", html, StringComparison.Ordinal);
        Assert.Contains("shrink-0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_does_not_scroll_away_either() =>
        Assert.Contains("shrink-0", UiSidebarHeader[Div["x"]].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Everything_the_rail_takes_away_is_marked_by_whoever_owns_the_words()
    {
        // The rule hides .ui-rail-hide and nothing else, so a component that forgot the mark keeps its words in
        // a 4.5rem rail — which is why this asserts on all four at once rather than one at a time.
        Assert.Contains("ui-rail-hide", UiNavItem.Label("Overview").Href("/").ToHtml(), StringComparison.Ordinal);
        Assert.Contains("ui-rail-hide", UiBrand.Label("Rask").Href("/").ToHtml(), StringComparison.Ordinal);
        Assert.Contains("ui-rail-hide", UiNavGroup.Heading("Data")[Li].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("ui-rail-hide", UiProfile.Name("Ada Lovelace").ToHtml(), StringComparison.Ordinal);
    }
}
