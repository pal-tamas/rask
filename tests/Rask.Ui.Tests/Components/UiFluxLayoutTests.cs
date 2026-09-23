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

    // ---- separator ------------------------------------------------------------------------------

    [Fact]
    public void A_plain_divider_is_a_separator_and_a_worded_one_is_text()
    {
        Assert.Contains("role=\"separator\"", Ui.Divider.ToHtml());
        Assert.Contains("aria-orientation=\"vertical\"", Ui.Divider.Vertical(true).ToHtml());
        // A separator's content is not read, so a divider carrying words keeps them as text.
        Assert.DoesNotContain("role=", Ui.Divider.Text("or").ToHtml());
    }

    [Fact]
    public void A_divider_can_be_subtle_and_put_its_word_at_one_end()
    {
        var html = Ui.Divider.Text("then").Subtle(true).Align(Ui.Align.Start).ToHtml();

        Assert.Contains("ui-divider-subtle", html);
        Assert.Contains("divider-start", html);
        Assert.Contains("divider-end", Ui.Divider.Text("then").Align(Ui.Align.End).ToHtml());
        Assert.DoesNotContain("divider-center", Ui.Divider.Text("then").Align(Ui.Align.Center).ToHtml());
    }

    [Fact]
    public void The_kit_stylesheet_takes_daisyUIs_margin_off_the_divider() =>
        // We style, you space: daisyUI's 1rem margin is zeroed at the variable it is built from.
        Assert.Matches(@"\.divider[^{]*\{[^}]*--divider-m:\s*0", UiStylesheet.Css);

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

        var current = Ui.NavItem.Label("Orders").Href("/orders").Icon(Ui.IconName.Book).Badge("12").ToHtml();
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
        var plain = Ui.NavGroup.Heading("Settings")[Ui.NavItem.Label("Profile").Href("/profile")].ToHtml();
        Assert.Contains("menu-title", plain);
        Assert.DoesNotContain("<details", plain);

        var folding = Ui.NavGroup.Heading("Settings").Expandable(true)[Ui.NavItem.Label("Profile").Href("/profile")].ToHtml();
        Assert.Contains("<details open>", folding);
        Assert.Contains("<summary>", folding);

        Assert.Contains(
            "<details>",
            Ui.NavGroup.Heading("Settings").Expandable(true).Expanded(false)[Ui.NavItem.Label("P").Href("/p")].ToHtml());
    }

    // ---- type -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "<div ")]
    [InlineData(1, "<h1 ")]
    [InlineData(3, "<h3 ")]
    [InlineData(6, "<h6 ")]
    public void A_headings_level_is_its_element_and_its_size_is_separate(int? level, string expected)
    {
        var html = Ui.Heading.Level(level).Size(Ui.Size.Xl)["Orders"].ToHtml();

        Assert.StartsWith(expected, html, StringComparison.Ordinal);
        Assert.Contains("text-2xl", html);
    }

    [Fact]
    public void Subheading_and_text_stay_out_of_the_outline()
    {
        Assert.StartsWith("<div class=\"text-base-content/60", Ui.Subheading["Everything you have ordered"].ToHtml(), StringComparison.Ordinal);
        Assert.StartsWith("<p ", Ui.Text["Body"].ToHtml(), StringComparison.Ordinal);
        Assert.StartsWith("<span ", Ui.Text.Inline(true)["run"].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("text-error", Ui.Text.Tone(Ui.Tone.Error)["Failed"].ToHtml());
        Assert.Contains("font-medium", Ui.Text.Strong(true)["Total"].ToHtml());
    }

    [Fact]
    public void A_page_header_and_a_card_take_a_heading_level()
    {
        Assert.Contains("<h1", Ui.Header.Heading("Orders").ToHtml());
        Assert.Contains("<h2", Ui.Header.Heading("Orders").HeadingLevel(2).ToHtml());
        Assert.Contains("<h2", Ui.Card.Heading("Total").ToHtml());
        Assert.Contains("<h3", Ui.Card.Heading("Total").HeadingLevel(3).ToHtml());
    }
}
