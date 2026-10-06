using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiBadge" /> and <see cref="UiAlert" /> ARE their elements, and what they say is their children.
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

    [Fact]
    public void The_kit_stylesheet_sizes_an_icon_nobody_sized_by_the_daisy_component_it_sits_in()
    {
        // In a daisy-drawn component the icon is a child the kit does not build, so the compiled sheet sizes
        // it — keyed to an icon whose call site wrote no size after Ui.Icon's own classes, so one sized on
        // purpose is left alone. `.btn` is what the kit's remaining hand-written daisy buttons carry; Flux's
        // Ui.Button sizes its own icon.
        const string unsized = @"svg\[data-ui-icon\]:not\(\[class\*=\\ size-\],\[class\*=\\ w-\],\[class\*=\\ h-\]\)";

        Assert.Matches(new Regex(@"\.btn>" + unsized + @"[^{]*\{width:1rem"), UiStylesheet.Css);
        Assert.Matches(new Regex(@"\.badge>" + unsized + @"[^{]*\{width:1em"), UiStylesheet.Css);
        Assert.Matches(new Regex(@"\.alert>" + unsized + @"\{width:1\.25rem"), UiStylesheet.Css);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("me-1", true)]
    [InlineData("text-ui-muted opacity-60", true)]
    [InlineData("size-4", false)]
    [InlineData("me-1 size-3.5", false)]
    [InlineData("h-4 w-4", false)]
    public void An_icon_counts_as_unsized_exactly_when_its_call_site_named_no_size(string? extra, bool unsized)
    {
        // The stylesheet rule above reads the class attribute, so what it reads is pinned here: a size
        // from the call site always follows a space, and Ui.Icon's own default never does.
        var classes = Regex.Match(Ui.Icon.Name(Ui.IconName.Check).Class(extra).ToHtml(), "class=\"([^\"]*)\"").Groups[1].Value;

        var sized = classes.Contains(" size-", StringComparison.Ordinal)
            || classes.Contains(" w-", StringComparison.Ordinal)
            || classes.Contains(" h-", StringComparison.Ordinal);

        Assert.Equal(unsized, !sized);
    }
}
