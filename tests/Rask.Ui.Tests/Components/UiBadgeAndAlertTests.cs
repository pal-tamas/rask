using System.Text.RegularExpressions;

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
