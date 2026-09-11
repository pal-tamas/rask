namespace Rask.Ui.Tests.Components;

/// <summary>
///     <see cref="UiBadge" /> and <see cref="UiAlert" /> ARE their elements, and what they say is their children.
/// </summary>
public partial class UiBadgeAndAlertTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_badge_is_one_span_showing_its_children() =>
        Assert.Equal("<span class=\"badge badge-success\">Live</span>", UiBadge.Tone(UiTone.Success)["Live"].ToHtml());

    [Fact]
    public void Element_steps_reach_the_badge() =>
        Assert.Equal(
            "<span id=\"count\" class=\"badge ms-2\" data-testid=\"count\">9</span>",
            UiBadge.Id("count").Class("ms-2").Data("testid", "count")["9"].ToHtml());

    [Theory]
    [InlineData(UiTone.Error, "alert")]
    [InlineData(UiTone.Warning, "alert")]
    [InlineData(UiTone.Info, "status")]
    [InlineData(UiTone.Success, "status")]
    public void An_alerts_role_follows_its_tone(UiTone tone, string role)
    {
        // `alert` interrupts a screen reader, which is right for a failure and rude for an explanation.
        Assert.Contains($"role=\"{role}\"", UiAlert.Tone(tone)["x"].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_alert_with_no_tone_announces_politely() =>
        Assert.Contains("role=\"status\"", UiAlert["x"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_role_the_call_site_set_wins_over_the_one_the_tone_implies()
    {
        // The Role step used to be unreachable on an alert: the kit wrote the role and offered no way to
        // change it. Now it is Element's, and the tone only supplies the default.
        var html = UiAlert.Tone(UiTone.Error).Role("note")["x"].ToHtml();

        Assert.Contains("role=\"note\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"alert\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_leaves_the_call_sites_role_as_it_was()
    {
        // The derived role stands in only while the attributes are written, so the next render derives it
        // again from the tone rather than reading back the one the last render put there.
        var alert = UiAlert.Tone(UiTone.Error);
        var first = alert["x"].ToHtml();

        Assert.Null(alert.Role);
        Assert.Equal(first, alert.ToHtml());
    }

    [Fact]
    public void An_alert_keeps_the_documented_attribute_order() =>
        Assert.Equal(
            "<div id=\"a\" class=\"alert alert-error\" data-testid=\"a\" role=\"alert\"><span>Payment failed</span></div>",
            UiAlert.Id("a").Tone(UiTone.Error).Data("testid", "a")[Span["Payment failed"]].ToHtml());
}
