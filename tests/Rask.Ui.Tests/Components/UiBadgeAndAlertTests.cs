namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiAlert" /> IS its element, and what it says is its children. The badge's half of this
///     file moved to <see cref="UiBadgeTests" /> when the badge became Flux's.
/// </summary>
public partial class UiBadgeAndAlertTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(Ui.Tone.Error, "alert")]
    [InlineData(Ui.Tone.Warning, "alert")]
    [InlineData(Ui.Tone.Info, "status")]
    [InlineData(Ui.Tone.Success, "status")]
    public void An_alerts_role_follows_its_tone(Ui.Tone tone, string role)
    {
        // `alert` interrupts a screen reader, which is right for a failure and rude for an explanation.
        Assert.Contains($"role=\"{role}\"", Ui.Alert.Tone(tone)["x"].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_alert_with_no_tone_announces_politely() =>
        Assert.Contains("role=\"status\"", Ui.Alert["x"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_role_the_call_site_set_wins_over_the_one_the_tone_implies()
    {
        // The Role step used to be unreachable on an alert: the kit wrote the role and offered no way to
        // change it. Now it is Element's, and the tone only supplies the default.
        var html = Ui.Alert.Tone(Ui.Tone.Error).Role("note")["x"].ToHtml();

        Assert.Contains("role=\"note\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"alert\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_leaves_the_call_sites_role_as_it_was()
    {
        // The derived role stands in only while the attributes are written, so the next render derives it
        // again from the tone rather than reading back the one the last render put there.
        var alert = Ui.Alert.Tone(Ui.Tone.Error);
        var first = alert["x"].ToHtml();

        Assert.Null(alert.Role);
        Assert.Equal(first, alert.ToHtml());
    }

    [Fact]
    public void An_alert_keeps_the_documented_attribute_order() =>
        Assert.Equal(
            "<div id=\"a\" class=\"alert alert-error\" data-testid=\"a\" role=\"alert\"><span>Payment failed</span></div>",
            Ui.Alert.Id("a").Tone(Ui.Tone.Error).Data("testid", "a")[Span["Payment failed"]].ToHtml());
}
