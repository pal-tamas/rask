namespace Rask.Ui.Tests.Components;

public partial class UiCollapseTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_leaves_the_open_state_to_the_browser()
    {
        var html = UiCollapse.Title("Advanced")[P["…"]].ToHtml();

        Assert.DoesNotContain("collapse-open", html);
        Assert.DoesNotContain("collapse-close", html);
    }

    [Fact]
    public void Open_writes_the_open_class() =>
        Assert.Contains("collapse-open", UiCollapse.Title("Advanced").Open(true)[P["…"]].ToHtml());

    [Fact]
    public void Closed_writes_collapse_close_rather_than_merely_omitting_collapse_open()
    {
        var html = UiCollapse.Title("Advanced").Open(false)[P["…"]].ToHtml();

        Assert.Contains("collapse-close", html);
        Assert.DoesNotContain("collapse-open", html);
    }

    [Fact]
    public void It_is_no_longer_a_details_element()
    {
        // <details> opened and closed with no runtime, and could not say WHICH section was open — so a
        // page could neither restore that nor react to it.
        var html = UiCollapse.Title("Advanced")[P["…"]].ToHtml();

        Assert.DoesNotContain("<details", html);
        Assert.DoesNotContain("<summary", html);
    }

    [Fact]
    public void The_heading_is_a_button_that_announces_its_state()
    {
        Assert.Contains("aria-expanded=\"true\"",
            UiCollapse.Title("Advanced").Open(true)[P["…"]].ToHtml());
        Assert.Contains("aria-expanded=\"false\"",
            UiCollapse.Title("Advanced").Open(false)[P["…"]].ToHtml());
    }

    [Theory]
    [InlineData(UiMarker.Arrow, "collapse-arrow")]
    [InlineData(UiMarker.Plus, "collapse-plus")]
    public void Every_marker_writes_its_own_class(UiMarker marker, string expected) =>
        Assert.Contains(expected, UiCollapse.Title("Advanced").Marker(marker)[P["…"]].ToHtml());

    [Fact]
    public void No_marker_is_the_default_and_writes_nothing() =>
        Assert.DoesNotContain("collapse-arrow",
            UiCollapse.Title("Advanced").Marker(UiMarker.None)[P["…"]].ToHtml());

    [Fact]
    public void The_title_and_the_content_are_daisyUIs_two_parts()
    {
        var html = UiCollapse.Title("Advanced")[P["hidden thing"]].ToHtml();

        Assert.Contains("collapse-title", html);
        Assert.Contains("collapse-content", html);
        Assert.Contains("hidden thing", html);
    }
}
