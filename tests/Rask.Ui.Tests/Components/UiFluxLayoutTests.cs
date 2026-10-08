namespace Rask.UiTests.Components;

/// <summary>
///     The layout pieces taken from Flux UI: the separator, the spacer, the sidebar and its toggle, the navigation
///     list, and the type scale.
/// </summary>
public partial class UiFluxLayoutTests : global::Rask.Core.RaskMarkup
{
    // ---- spacer ---------------------------------------------------------------------------------

    [Fact]
    public void A_spacer_grows_and_is_hidden_from_assistive_tech() =>
        Assert.Equal("<div class=\"flex-1\" aria-hidden=\"true\"></div>", Ui.Spacer.ToHtml());

    // ---- sidebar --------------------------------------------------------------------------------

    [Fact]
    public void A_sidebar_is_an_aside_beside_the_page_docked_from_its_breakpoint()
    {
        var html = Ui.Sidebar.Id("nav").Page(Main["page"]).Collapsible(Ui.Breakpoint.Lg)[
            Ui.Navlist[Ui.NavlistItem.Href("/")["Home"]]
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
