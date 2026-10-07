namespace Rask.UiTests.Components;

/// <summary>
///     Flux's header and main, and the grid the three layout parts make of whatever holds them.
/// </summary>
/// <remarks>
///     The look is held to Flux by <c>scripts/flux/parity.mjs layouts/header</c>. Pinned here: the landmarks,
///     what each prop writes, and the rules of the layout grid — which live in the kit's sheet, not in a class.
/// </remarks>
public partial class UiHeaderTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_header_is_the_banner_landmark_in_the_header_area()
    {
        var html = Ui.Header[Span["x"]].ToHtml();

        Assert.StartsWith("<header", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-header", html, StringComparison.Ordinal);
        Assert.Contains("[grid-area:header]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_header_holds_its_content_to_the_container_while_its_ground_runs_edge_to_edge()
    {
        var html = Ui.Header.Container(true)[Span["x"]].ToHtml();

        Assert.Contains("<div class=\"mx-auto flex h-14 w-full", html, StringComparison.Ordinal);
        Assert.Contains("max-w-7xl", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sticky_header_stays_at_the_top()
    {
        var html = Ui.Header.Sticky(true)[Span["x"]].ToHtml();

        Assert.Contains("sticky top-0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_is_the_element_Flux_writes_and_takes_the_main_area()
    {
        var html = Ui.Main[Span["x"]].ToHtml();

        Assert.StartsWith("<div", html, StringComparison.Ordinal);
        Assert.Contains("[grid-area:main]", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-main", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inset_main_is_a_panel_and_says_so_to_the_grid()
    {
        var html = Ui.Main.Inset(true)[Span["x"]].ToHtml();

        Assert.Contains("data-inset", html, StringComparison.Ordinal);
        Assert.Contains("lg:rounded-xl", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inset_container_centres_the_content_inside_the_full_width_panel()
    {
        var html = Ui.Main.Inset(true).Container(true)[Span["x"]].ToHtml();

        Assert.Contains("<div class=\"mx-auto", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Whatever_holds_a_main_is_the_layout_grid()
    {
        var css = UiStylesheet.Css;

        Assert.Contains(":where(:has(>[data-ui-main]))", css, StringComparison.Ordinal);
        Assert.Contains("\"sidebar main aside\"", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_written_after_the_sidebar_sits_beside_it()
    {
        var css = UiStylesheet.Css;

        Assert.Contains("[data-ui-sidebar]~[data-ui-header]", css, StringComparison.Ordinal);
        Assert.Contains("\"sidebar header header\"", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sidebar_states_are_variants_the_parts_are_written_with()
    {
        var css = UiStylesheet.Css;

        Assert.Contains("[data-ui-sidebar-rail]:checked", css, StringComparison.Ordinal);
        Assert.Contains("[data-ui-sidebar-open]:checked", css, StringComparison.Ordinal);
    }
}
