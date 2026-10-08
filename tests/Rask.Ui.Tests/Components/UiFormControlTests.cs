namespace Rask.UiTests.Components;

/// <summary>
///     The daisyUI-drawn form controls, at daisyUI class parity. Ui.Input, Ui.Textarea, Ui.Checkbox, Ui.Radio and Ui.Switch are Flux's: UiInputTests, UiCheckboxTests, UiRadioTests, UiSwitchTests.
/// </summary>
public partial class UiFormControlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_file_input_takes_the_ghost_variant_too() =>
        Assert.Contains("file-input-ghost", Ui.FileInput.Value("").Label("Avatar").Variant(Ui.Variant.Ghost).ToHtml());

    [Fact]
    public void A_range_can_stand_on_end() =>
        Assert.Contains("range-vertical", Ui.Range.Value(0d).Label("Volume").Vertical(true).ToHtml());

    [Fact]
    public void A_horizontal_range_writes_no_direction_class() =>
        Assert.DoesNotContain("range-vertical", Ui.Range.Value(0d).Label("Volume").ToHtml());

    [Fact]
    public void A_filter_option_carries_its_value_exactly_once()
    {
        // It set the value through the escape hatch while the chain's Value was believed to carry the
        // checked state. With the type pinned as well, the attribute was written TWICE —
        // `value="bug" ... value="False"` — which is invalid HTML that happens to work.
        var html = Filter(selected: "bug");

        Assert.Equal(1, Occurrences(html, "value=\"bug\""));
        Assert.DoesNotContain("value=\"False\"", html);
    }

    [Theory]
    [InlineData("file")]
    public void A_text_control_carries_the_class_the_hint_reads(string kind)
    {
        // daisyUI shows `.validator-hint` only next to a `.validator` control that is invalid. No kit
        // control wrote the class, so the hint was `visibility: hidden` forever — a component with a
        // REQUIRED message that could not be read. `.validator` alone is inert: it only sets a colour
        // variable under :user-valid/:user-invalid, so an untouched field looks exactly as before.
        Assert.Contains("validator", Control(kind, Ui.Tone.Neutral));
    }

    [Theory]
    [InlineData("file")]
    public void An_errored_control_says_so_to_a_screen_reader_as_well_as_in_colour(string kind)
    {
        // Also what makes daisyUI reveal the hint from the KIT's own tone rather than only from the
        // browser's native validity. A field that is visibly red and announces nothing is half a
        // message.
        Assert.Contains("aria-invalid=\"true\"", Control(kind, Ui.Tone.Error));
    }

    [Theory]
    [InlineData("file")]
    public void A_control_with_no_error_does_not_claim_one(string kind) =>
        Assert.DoesNotContain("aria-invalid", Control(kind, Ui.Tone.Neutral));

    [Fact]
    public void The_hint_keeps_its_space_whether_or_not_it_is_showing()
    {
        // daisyUI hides it with `visibility`, not `display`, so a form does not jump as the reader
        // types. That is the reason the hint is rendered rather than conditionally omitted.
        Assert.Contains("validator-hint", Ui.Validator.Message("Enter a valid email").ToHtml());
    }

    [Theory]
    [InlineData(Ui.MaskShape.Circle, "mask-circle")]
    [InlineData(Ui.MaskShape.Squircle, "mask-squircle")]
    [InlineData(Ui.MaskShape.Star2, "mask-star-2")]
    [InlineData(Ui.MaskShape.Hexagon2, "mask-hexagon-2")]
    [InlineData(Ui.MaskShape.Triangle4, "mask-triangle-4")]
    [InlineData(Ui.MaskShape.Half1, "mask-half-1")]
    public void Every_mask_shape_writes_its_own_class(Ui.MaskShape shape, string expected) =>
        Assert.Contains(expected, Ui.Mask.Shape(shape)[Span["x"]].ToHtml());

    [Fact]
    public void A_filter_is_a_radio_group_with_a_reset()
    {
        // Radios rather than buttons is what lets daisyUI hide the unpicked options in CSS, and gives
        // the keyboard arrow-key behaviour a row of buttons would have to reimplement.
        var html = Filter(selected: null);

        Assert.Contains("class=\"filter\"", html);
        Assert.Contains("filter-reset", html);
        Assert.Equal(4, Occurrences(html, "type=\"radio\""));
    }

    [Fact]
    public void The_filter_reset_is_the_chosen_one_when_nothing_else_is() =>
        Assert.Contains("aria-label=\"All\"", Filter(selected: null));

    [Fact]
    public void The_filter_carries_each_option_as_its_own_value_and_name()
    {
        // daisyUI draws the option's text from the input's value, so it is the label as well.
        var html = Filter(selected: "bug");

        Assert.Contains("value=\"bug\"", html);
        Assert.Contains("aria-label=\"bug\"", html);
    }

    [Fact]
    public void A_filter_is_not_a_nested_form()
    {
        // daisyUI's example wraps this in <form> so a reset BUTTON can clear it. The reset here is a
        // radio carrying filter-reset, and a <form> inside somebody else's form is invalid HTML.
        Assert.DoesNotContain("<form", Filter(selected: null));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string Control(string kind, Ui.Tone tone) =>
        string.Equals(kind, "file", StringComparison.Ordinal)
            ? Ui.FileInput.Value("").Label("Avatar").Tone(tone).ToHtml()
            : throw new ArgumentOutOfRangeException(nameof(kind));

    private static string Filter(string? selected) =>
        Ui.Filter.Value(selected).Group("tags")
            .Options([("bug", "bug"), ("feature", "feature"), ("docs", "docs")])
            .ToHtml();
}
