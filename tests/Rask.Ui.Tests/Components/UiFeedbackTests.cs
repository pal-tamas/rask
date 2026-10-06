namespace Rask.UiTests.Components;

/// <summary>
///     The feedback components (the tooltip has its own file). Most of these assertions are about what gets ANNOUNCED, because
///     that is the half of feedback a purely visual check never sees.
/// </summary>
public partial class UiFeedbackTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(Ui.LoadingShape.Spinner, "loading-spinner")]
    [InlineData(Ui.LoadingShape.Dots, "loading-dots")]
    [InlineData(Ui.LoadingShape.Ring, "loading-ring")]
    [InlineData(Ui.LoadingShape.Ball, "loading-ball")]
    [InlineData(Ui.LoadingShape.Bars, "loading-bars")]
    [InlineData(Ui.LoadingShape.Infinity, "loading-infinity")]
    public void Every_loading_shape_writes_its_own_class(Ui.LoadingShape shape, string expected) =>
        Assert.Contains(expected, Ui.Loading.Text("Loading orders").Shape(shape).ToHtml());

    [Fact]
    public void A_loading_indicator_with_no_shape_still_gets_one()
    {
        // The class is what draws it: `loading` alone is an unstyled span, so falling back to nothing
        // would render an indicator that indicates nothing.
        Assert.Contains("loading-spinner", Ui.Loading.Text("Loading orders").ToHtml());
    }

    [Fact]
    public void The_spinner_is_hidden_and_the_words_are_what_is_announced()
    {
        // A bare spinner tells a screen reader nothing at all, and "Loading orders" read once is worth
        // more than an animation.
        var html = Ui.Loading.Text("Loading orders").ToHtml();

        Assert.Contains("role=\"status\"", html);
        Assert.Contains("aria-hidden=\"true\"", html);
        Assert.Contains("Loading orders", html);
    }

    [Theory]
    [InlineData(Ui.Tone.Error, "alert-error")]
    [InlineData(Ui.Tone.Warning, "alert-warning")]
    [InlineData(Ui.Tone.Success, "alert-success")]
    [InlineData(Ui.Tone.Info, "alert-info")]
    public void Every_alert_tone_writes_its_own_class(Ui.Tone tone, string expected) =>
        Assert.Contains(expected, Ui.Alert.Tone(tone)["Payment failed"].ToHtml());

    [Fact]
    public void A_progress_bar_is_a_real_progress_element()
    {
        // It reports its own value to assistive technology, which a styled div has to be told to do and
        // usually is not.
        var html = Ui.Progress.Label("Upload").Value(62).Max(100).ToHtml();

        Assert.Contains("<progress", html);
        Assert.Contains("value=\"62\"", html);
    }

    [Fact]
    public void A_radial_progress_says_its_number_rather_than_only_drawing_it() =>
        Assert.Contains("78", Ui.RadialProgress.Label("Disk used").Percent(78).ToHtml());

    [Fact]
    public void A_skeleton_carries_the_base_class() =>
        Assert.Contains("skeleton", Ui.Skeleton.Class("h-24 w-full").ToHtml());

    [Fact]
    public void A_toast_is_announced_politely_rather_than_interrupting()
    {
        // role=status, not alert: this is the outcome of something the reader just did, so it should be
        // read at the next opportunity rather than cutting across what they are hearing.
        Assert.Contains("role=\"status\"", Ui.Toast.Message("Saved").ToHtml());
    }

    [Fact]
    public void A_toast_only_offers_a_dismiss_when_there_is_something_to_dismiss_it_with()
    {
        Assert.DoesNotContain("Dismiss", Ui.Toast.Message("Saved").ToHtml());
        Assert.Contains("Dismiss", Ui.Toast.Message("Saved").OnDismiss(() => { }).ToHtml());
    }

    [Fact]
    public void A_failed_toast_changes_its_icon_and_not_only_its_colour()
    {
        // The shape is what carries the outcome; the colour only reinforces it. Colour alone would be
        // invisible to a reader who cannot distinguish it.
        Assert.NotEqual(
            Ui.Toast.Message("Saved").ToHtml(),
            Ui.Toast.Message("Saved").Tone(Ui.Tone.Error).ToHtml());
    }
}
