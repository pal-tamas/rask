using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>
///     The layout pieces taken from Flux UI: the separator, the spacer, the sidebar and its toggle, the navigation
///     list, and the type scale.
/// </summary>
public partial class UiFluxLayoutTests : global::Rask.Core.RaskMarkup
{
    private sealed class StubComponent(global::Rask.Core.Component inner) : global::Rask.Core.Component
    {
        protected override global::Rask.Core.Component? Render() => inner;
    }

    private static IDisposable OnPage(string path)
    {
        var services = new ServiceCollection().AddSingleton(new RouteState { Path = path }).BuildServiceProvider();
        return LiveRenderContext.Begin(new StubComponent(Span), services);
    }

    // ---- spacer ---------------------------------------------------------------------------------

    [Fact]
    public void A_spacer_grows_and_is_hidden_from_assistive_tech() =>
        Assert.Equal("<div class=\"flex-1\" aria-hidden=\"true\"></div>", Ui.Spacer.ToHtml());

    // ---- sidebar --------------------------------------------------------------------------------

    [Fact]
    public void A_sidebar_is_an_aside_beside_the_page_docked_from_its_breakpoint()
    {
        var html = Ui.Sidebar.Id("nav").Page(Main["page"]).Collapsible(Ui.Breakpoint.Lg)[
            Ui.NavList[Ui.NavItem.Label("Home").Href("/")]
        ].ToHtml();

        Assert.Contains("lg:drawer-open", html);
        Assert.Contains("<aside", html);
        Assert.Contains("aria-label=\"Sidebar\"", html);
        Assert.Contains("id=\"nav\"", html);
        Assert.Contains("for=\"nav\"", html);
        Assert.True(
            html.IndexOf("<main>page</main>", StringComparison.Ordinal) < html.IndexOf("<aside", StringComparison.Ordinal),
            "the page is the drawer's content and the sidebar its side.");
    }

    [Fact]
    public void A_sidebar_with_no_breakpoint_is_always_docked() =>
        Assert.Contains("drawer-open", Ui.Sidebar.Id("nav").Page(Main["page"]).ToHtml());

    [Fact]
    public void The_toggle_is_a_keyboard_reachable_label_that_hides_where_the_sidebar_docks()
    {
        var html = Ui.SidebarToggle.For("nav").Collapsible(Ui.Breakpoint.Lg).ToHtml();

        Assert.StartsWith("<label", html, StringComparison.Ordinal);
        Assert.Contains("for=\"nav\"", html);
        Assert.Contains("role=\"button\"", html);
        Assert.Contains("tabindex=\"0\"", html);
        Assert.Contains("aria-label=\"Toggle sidebar\"", html);
        Assert.Contains("lg:hidden", html);
    }

    // ---- navigation -----------------------------------------------------------------------------

    [Fact]
    public void A_nav_list_is_a_named_nav_landmark_around_a_menu()
    {
        var html = Ui.NavList.AccessibleLabel("Main")[Ui.NavItem.Label("Home").Href("/")].ToHtml();

        Assert.StartsWith("<nav aria-label=\"Main\"><ul class=\"menu", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_item_for_the_page_being_shown_is_current_without_being_told()
    {
        using var _ = OnPage("/orders");

        var current = Ui.NavItem.Label("Orders").Href("/orders").Icon(Ui.IconName.BookOpen).Badge("12").ToHtml();
        var other = Ui.NavItem.Label("Customers").Href("/customers").ToHtml();

        Assert.Contains("menu-active", current);
        Assert.Contains("aria-current=\"page\"", current);
        Assert.Contains("badge", current);
        Assert.DoesNotContain("aria-current", other);
        Assert.DoesNotContain("menu-active", other);
    }

    [Fact]
    public void A_section_item_stays_current_under_its_prefix()
    {
        using var _ = OnPage("/orders/42");

        Assert.Contains(
            "aria-current=\"page\"",
            Ui.NavItem.Label("Orders").Href("/orders").MatchPrefix(true).ToHtml());
    }

    [Fact]
    public void Current_can_be_stated_either_way()
    {
        using var _ = OnPage("/orders");

        Assert.DoesNotContain("aria-current", Ui.NavItem.Label("Orders").Href("/orders").Current(false).ToHtml());
        Assert.Contains("aria-current=\"page\"", Ui.NavItem.Label("Help").Href("/help").Current(true).ToHtml());
    }

    [Fact]
    public void A_nav_group_is_a_heading_or_a_disclosure()
    {
        var plain = Ui.NavGroup.Title("Settings")[Ui.NavItem.Label("Profile").Href("/profile")].ToHtml();
        Assert.Contains("menu-title", plain);
        Assert.DoesNotContain("<details", plain);

        var folding = Ui.NavGroup.Title("Settings").Expandable(true)[Ui.NavItem.Label("Profile").Href("/profile")].ToHtml();
        Assert.Contains("<details open>", folding);
        Assert.Contains("<summary>", folding);

        Assert.Contains(
            "<details>",
            Ui.NavGroup.Title("Settings").Expandable(true).Expanded(false)[Ui.NavItem.Label("P").Href("/p")].ToHtml());
    }

    // ---- type -----------------------------------------------------------------------------------

    [Fact]
    public void A_page_header_and_a_card_heading_take_a_heading_level()
    {
        Assert.Contains("<h1", Ui.Header.Title("Orders").ToHtml());
        Assert.Contains("<h2", Ui.Header.Title("Orders").TitleLevel(2).ToHtml());
        Assert.Contains("<div", Ui.CardHeading["Total"].ToHtml());
        Assert.Contains("<h3", Ui.CardHeading.Level(3)["Total"].ToHtml());
    }
}
