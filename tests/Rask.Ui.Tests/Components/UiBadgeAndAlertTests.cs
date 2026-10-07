namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiBadge" /> IS its element, and what it says is its children. (The alert this file also
///     covered is <see cref="UiCallout" /> now: see <c>UiCalloutTests</c>.)
/// </summary>
public partial class UiBadgeAndAlertTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_badge_is_one_span_showing_its_children() =>
        Assert.Equal("<span class=\"badge badge-success\">Live</span>", Ui.Badge.Tone(Ui.Tone.Success)["Live"].ToHtml());

    [Fact]
    public void Element_steps_reach_the_badge() =>
        Assert.Equal(
            "<span id=\"count\" class=\"badge ms-2\" data-testid=\"count\">9</span>",
            Ui.Badge.Id("count").Class("ms-2").Data("testid", "count")["9"].ToHtml());
}
