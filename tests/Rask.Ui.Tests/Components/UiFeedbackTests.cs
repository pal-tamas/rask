namespace Rask.Ui.Tests.Components;

/// <summary>
///     The seven feedback components. Most of these assertions are about what gets ANNOUNCED, because
///     that is the half of feedback a purely visual check never sees.
/// </summary>
public partial class UiFeedbackTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(UiLoadingShape.Spinner, "loading-spinner")]
    [InlineData(UiLoadingShape.Dots, "loading-dots")]
    [InlineData(UiLoadingShape.Ring, "loading-ring")]
    [InlineData(UiLoadingShape.Ball, "loading-ball")]
    [InlineData(UiLoadingShape.Bars, "loading-bars")]
    [InlineData(UiLoadingShape.Infinity, "loading-infinity")]
    public void Every_loading_shape_writes_its_own_class(UiLoadingShape shape, string expected) =>
        Assert.Contains(expected, UiLoading.Text("Loading orders").Shape(shape).ToHtml());

    [Fact]
    public void A_loading_indicator_with_no_shape_still_gets_one()
    {
        // The class is what draws it: `loading` alone is an unstyled span, so falling back to nothing
        // would render an indicator that indicates nothing.
        Assert.Contains("loading-spinner", UiLoading.Text("Loading orders").ToHtml());
    }

    [Fact]
    public void The_spinner_is_hidden_and_the_words_are_what_is_announced()
    {
        // A bare spinner tells a screen reader nothing at all, and "Loading orders" read once is worth
        // more than an animation.
        var html = UiLoading.Text("Loading orders").ToHtml();

        Assert.Contains("role=\"status\"", html);
        Assert.Contains("aria-hidden=\"true\"", html);
        Assert.Contains("Loading orders", html);
    }

    [Theory]
    [InlineData(UiPlacement.Top, "tooltip-top")]
    [InlineData(UiPlacement.Bottom, "tooltip-bottom")]
    [InlineData(UiPlacement.Left, "tooltip-left")]
    [InlineData(UiPlacement.Right, "tooltip-right")]
    [InlineData(UiPlacement.Start, "tooltip-start")]
    [InlineData(UiPlacement.Center, "tooltip-center")]
    [InlineData(UiPlacement.End, "tooltip-end")]
    public void A_tooltip_defines_all_seven_placements(UiPlacement placement, string expected) =>
        Assert.Contains(expected, UiTooltip.Tip("Copy").Placement(placement)[Span["c"]].ToHtml());

    [Fact]
    public void A_tooltip_can_be_shown_without_a_hover()
    {
        // The only way a touch user ever sees one: there is no hover on a touch screen.
        Assert.Contains("tooltip-open", UiTooltip.Tip("Copy").Open(true)[Span["c"]].ToHtml());
        Assert.DoesNotContain("tooltip-open", UiTooltip.Tip("Copy")[Span["c"]].ToHtml());
    }

    [Fact]
    public void The_tip_travels_in_the_attribute_daisyUI_reads() =>
        Assert.Contains("data-tip=\"Copy\"", UiTooltip.Tip("Copy")[Span["c"]].ToHtml());

    [Theory]
    [InlineData(UiTone.Error, "alert-error")]
    [InlineData(UiTone.Warning, "alert-warning")]
    [InlineData(UiTone.Success, "alert-success")]
    [InlineData(UiTone.Info, "alert-info")]
    public void Every_alert_tone_writes_its_own_class(UiTone tone, string expected) =>
        Assert.Contains(expected, UiAlert.Tone(tone)["Payment failed"].ToHtml());

    [Fact]
    public void A_progress_bar_is_a_real_progress_element()
    {
        // It reports its own value to assistive technology, which a styled div has to be told to do and
        // usually is not.
        var html = UiProgress.Label("Upload").Value(62).Max(100).ToHtml();

        Assert.Contains("<progress", html);
        Assert.Contains("value=\"62\"", html);
    }

    [Fact]
    public void A_radial_progress_says_its_number_rather_than_only_drawing_it() =>
        Assert.Contains("78", UiRadialProgress.Label("Disk used").Percent(78).ToHtml());

    [Fact]
    public void A_skeleton_carries_the_base_class() =>
        Assert.Contains("skeleton", UiSkeleton.Class("h-24 w-full").ToHtml());

    [Fact]
    public void A_toast_is_announced_politely_rather_than_interrupting()
    {
        // role=status, not alert: this is the outcome of something the reader just did, so it should be
        // read at the next opportunity rather than cutting across what they are hearing.
        Assert.Contains("role=\"status\"", UiToast.Message("Saved").ToHtml());
    }

    [Fact]
    public void A_toast_only_offers_a_dismiss_when_there_is_something_to_dismiss_it_with()
    {
        Assert.DoesNotContain("Dismiss", UiToast.Message("Saved").ToHtml());
        Assert.Contains("Dismiss", UiToast.Message("Saved").Dismiss(() => { }).ToHtml());
    }

    [Fact]
    public void A_failed_toast_changes_its_icon_and_not_only_its_colour()
    {
        // The shape is what carries the outcome; the colour only reinforces it. Colour alone would be
        // invisible to a reader who cannot distinguish it.
        Assert.NotEqual(
            UiToast.Message("Saved").ToHtml(),
            UiToast.Message("Saved").Tone(UiTone.Error).ToHtml());
    }
}
