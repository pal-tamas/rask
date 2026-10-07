namespace Rask.UiTests.Components;

/// <summary>
///     The feedback components that are still daisyUI's (the tooltip is Flux's, in UiTooltipTests). Most of these
///     assertions are about what gets ANNOUNCED, because
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
}
