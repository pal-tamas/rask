namespace Rask.Ui.Tests.Components;

/// <summary>
///     The tab row, which stayed links.
/// </summary>
/// <remarks>
///     This is the one component in the kit that deliberately did NOT move onto C# state. A tab that is
///     a URL is bookmarkable, survives a refresh, answers the back button and works before the runtime
///     boots; a tab that is an index in a field is none of those. daisyUI's own tab supports the link
///     form, so nothing is given up by keeping it.
/// </remarks>
public partial class UiTabsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_row_is_daisyUIs_tabs_and_says_what_it_is()
    {
        var html = UiTabs[Tab("Live", active: true)].ToHtml();

        Assert.Contains("class=\"tabs", html);
        Assert.Contains("role=\"tablist\"", html);
    }

    [Fact]
    public void Each_tab_is_a_real_link()
    {
        // The property being tested is that it is an anchor with an href — not a button, not a div.
        var html = UiTabs[Tab("Live", active: true)].ToHtml();

        Assert.Contains("<a", html);
        Assert.Contains("href=\"/logs\"", html);
    }

    [Fact]
    public void The_active_tab_is_marked_for_CSS_and_for_a_screen_reader()
    {
        var html = Tab("Live", active: true).ToHtml();

        Assert.Contains("tab-active", html);
        Assert.Contains("aria-selected=\"true\"", html);
    }

    [Fact]
    public void An_inactive_tab_is_neither()
    {
        var html = Tab("Live", active: false).ToHtml();

        Assert.DoesNotContain("tab-active", html);
        Assert.Contains("aria-selected=\"false\"", html);
    }

    [Theory]
    [InlineData(UiTabStyle.Box, "tabs-box")]
    [InlineData(UiTabStyle.Border, "tabs-border")]
    [InlineData(UiTabStyle.Lift, "tabs-lift")]
    public void Every_style_writes_its_own_class(UiTabStyle style, string expected) =>
        Assert.Contains(expected, UiTabs.Style(style)[Tab("Live", active: true)].ToHtml());

    [Theory]
    [InlineData(UiSize.Xs, "tabs-xs")]
    [InlineData(UiSize.Xl, "tabs-xl")]
    public void Every_size_writes_its_own_class(UiSize size, string expected) =>
        Assert.Contains(expected, UiTabs.Size(size)[Tab("Live", active: true)].ToHtml());

    [Theory]
    [InlineData(UiPosition.Top, "tabs-top")]
    [InlineData(UiPosition.Bottom, "tabs-bottom")]
    public void The_two_positions_a_tab_row_has_write_their_class(UiPosition position, string expected) =>
        Assert.Contains(expected, UiTabs.Position(position)[Tab("Live", active: true)].ToHtml());

    [Theory]
    [InlineData(UiPosition.Left)]
    [InlineData(UiPosition.Right)]
    public void A_position_a_tab_row_has_no_class_for_writes_nothing(UiPosition position)
    {
        // Better than inventing `tabs-left`: a class daisyUI never defined is in the markup, absent
        // from the sheet, and does nothing — which reads as a working call site.
        var html = UiTabs.Position(position)[Tab("Live", active: true)].ToHtml();

        Assert.DoesNotContain("tabs-left", html);
        Assert.DoesNotContain("tabs-right", html);
    }

    [Fact]
    public void The_row_carries_no_outer_margin()
    {
        // We style, you space. The phone bleed (`-mx-3 px-3`) this row used to carry pushed it out of any
        // card it sat in; where a row sits is the page's to say.
        var html = UiTabs[Tab("Live", active: true)].ToHtml();
        var classes = html[(html.IndexOf("class=\"", StringComparison.Ordinal) + 7)..];
        classes = classes[..classes.IndexOf('"')];

        Assert.DoesNotContain(
            classes.Split(' '),
            c => c.StartsWith("-m", StringComparison.Ordinal)
                 || c.StartsWith("m-", StringComparison.Ordinal)
                 || c.StartsWith("mx-", StringComparison.Ordinal)
                 || c.StartsWith("sm:mx-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_count_rides_beside_the_label() =>
        Assert.Contains("12", Tab("Live", active: true, count: "12").ToHtml());

    [Fact]
    public void An_alarming_count_is_coloured_and_a_calm_one_is_not()
    {
        Assert.Contains("text-error", Tab("Live", true, "12", alarm: true).ToHtml());
        Assert.DoesNotContain("text-error", Tab("Live", true, "12").ToHtml());
    }

    [Fact]
    public void A_disabled_tab_is_drawn_as_unavailable() =>
        Assert.Contains("tab-disabled",
            UiTab.Href("/logs").Label("Live").Disabled(true).ToHtml());

    [Fact]
    public void A_tab_keeps_a_touch_sized_target_on_a_phone()
    {
        // daisyUI's tab is shorter than 44px, which is the smallest reliable touch target; the height
        // relaxes from sm up, where there is a pointer.
        Assert.Contains("min-h-11", Tab("Live", active: true).ToHtml());
    }

    private UiTab Tab(
        string label, bool active, string? count = null, bool? alarm = null) =>
        UiTab.Href("/logs").Label(label).Active(active).Count(count).Alarm(alarm);
}
