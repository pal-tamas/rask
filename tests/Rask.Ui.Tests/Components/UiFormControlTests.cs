namespace Rask.Ui.Tests.Components;

/// <summary>
///     The twelve form controls, at daisyUI class parity.
/// </summary>
public partial class UiFormControlTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(UiTone.Primary, "input-primary")]
    [InlineData(UiTone.Error, "input-error")]
    public void An_input_takes_a_tone(UiTone tone, string expected) =>
        Assert.Contains(expected, UiInput.Label("Email").Tone(tone).ToHtml());

    [Fact]
    public void An_input_takes_the_ghost_variant() =>
        Assert.Contains("input-ghost", UiInput.Label("Email").Variant(UiVariant.Ghost).ToHtml());

    [Fact]
    public void A_textarea_takes_the_ghost_variant_too()
    {
        // It could not before this: the table existed in UiClassNames with nothing calling it.
        Assert.Contains("textarea-ghost", UiTextarea.Label("Notes").Variant(UiVariant.Ghost).ToHtml());
    }

    [Fact]
    public void A_file_input_takes_the_ghost_variant_too() =>
        Assert.Contains("file-input-ghost", UiFileInput.Label("Avatar").Variant(UiVariant.Ghost).ToHtml());

    [Theory]
    [InlineData(UiVariant.Outline)]
    [InlineData(UiVariant.Soft)]
    [InlineData(UiVariant.Dash)]
    public void A_variant_daisyUI_has_no_text_control_class_for_writes_nothing(UiVariant variant)
    {
        // Better than inventing `input-outline`: the class would be in the markup, absent from the
        // sheet, and do nothing — which reads exactly like a working call site.
        var html = UiInput.Label("Email").Variant(variant).ToHtml();

        Assert.DoesNotContain("input-outline", html);
        Assert.DoesNotContain("input-soft", html);
        Assert.DoesNotContain("input-dash", html);
    }

    [Theory]
    [InlineData(UiSize.Xs, "input-xs")]
    [InlineData(UiSize.Xl, "input-xl")]
    public void An_input_takes_every_size(UiSize size, string expected) =>
        Assert.Contains(expected, UiInput.Label("Email").Size(size).ToHtml());

    [Theory]
    [InlineData(UiTone.Primary, "checkbox-primary")]
    [InlineData(UiTone.Success, "checkbox-success")]
    public void A_checkbox_takes_a_tone(UiTone tone, string expected) =>
        Assert.Contains(expected, UiCheckbox.Text("Remember me").Tone(tone).ToHtml());

    [Theory]
    [InlineData(UiTone.Primary, "toggle-primary")]
    [InlineData(UiTone.Warning, "toggle-warning")]
    public void A_toggle_takes_a_tone(UiTone tone, string expected) =>
        Assert.Contains(expected, UiToggle.Text("Email alerts").Tone(tone).ToHtml());

    [Theory]
    [InlineData(UiTone.Accent, "radio-accent")]
    [InlineData(UiTone.Info, "radio-info")]
    public void A_radio_takes_a_tone(UiTone tone, string expected) =>
        Assert.Contains(expected, UiRadio.Text("Standard").Group("shipping").Tone(tone).ToHtml());

    [Fact]
    public void A_range_can_stand_on_end() =>
        Assert.Contains("range-vertical", UiRange.Label("Volume").Vertical(true).ToHtml());

    [Fact]
    public void A_horizontal_range_writes_no_direction_class() =>
        Assert.DoesNotContain("range-vertical", UiRange.Label("Volume").ToHtml());

    [Theory]
    [InlineData("checkbox")]
    [InlineData("toggle")]
    [InlineData("radio")]
    public void A_control_the_page_says_is_on_renders_as_checked(string kind)
    {
        // These wrote `.Value(Checked == true)`, and on an <input> that is the VALUE attribute — so
        // every checked control in the kit rendered `value="True"` with no `checked` at all. It looked
        // right in C#, passed every markup assertion that did not name the attribute, and came out off
        // on a prerendered page. The checked state comes from `.Checked(...)`.
        var html = kind switch
        {
            "checkbox" => UiCheckbox.Text("Remember").Checked(true).ToHtml(),
            "toggle" => UiToggle.Text("Alerts").Checked(true).ToHtml(),
            _ => UiRadio.Text("Standard").Group("shipping").Checked(true).ToHtml(),
        };

        Assert.Contains("checked", html);
    }

    [Theory]
    [InlineData("checkbox")]
    [InlineData("toggle")]
    [InlineData("radio")]
    public void A_control_the_page_says_is_off_does_not(string kind)
    {
        var html = kind switch
        {
            "checkbox" => UiCheckbox.Text("Remember").Checked(false).ToHtml(),
            "toggle" => UiToggle.Text("Alerts").Checked(false).ToHtml(),
            _ => UiRadio.Text("Standard").Group("shipping").Checked(false).ToHtml(),
        };

        Assert.DoesNotContain("checked", html);
    }

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

    [Fact]
    public void A_checkbox_wraps_its_words_in_the_hit_target()
    {
        // On a phone a 16px box on its own is the difference between a control and a dare, so the label
        // has to be part of what you can press.
        var html = UiCheckbox.Text("Remember me").ToHtml();

        Assert.StartsWith("<label", html, StringComparison.Ordinal);
        Assert.Contains("Remember me", html);
    }

    [Theory]
    [InlineData(UiMaskShape.Circle, "mask-circle")]
    [InlineData(UiMaskShape.Squircle, "mask-squircle")]
    [InlineData(UiMaskShape.Star2, "mask-star-2")]
    [InlineData(UiMaskShape.Hexagon2, "mask-hexagon-2")]
    [InlineData(UiMaskShape.Triangle4, "mask-triangle-4")]
    [InlineData(UiMaskShape.Half1, "mask-half-1")]
    public void Every_mask_shape_writes_its_own_class(UiMaskShape shape, string expected) =>
        Assert.Contains(expected, UiMask.Shape(shape)[Span["x"]].ToHtml());

    [Fact]
    public void A_label_is_decoration_and_says_so_by_not_naming_anything()
    {
        // A <label> element names a control; this one styles text beside one. The control keeps its own
        // required name, which is why nothing here is aria-anything.
        var html = UiLabel.Text("Price").Trailing("EUR")[Span["field"]].ToHtml();

        Assert.Contains("class=\"label\"", html);
        Assert.Contains("Price", html);
        Assert.Contains("EUR", html);
    }

    [Fact]
    public void A_floating_label_puts_its_text_before_the_control()
    {
        // daisyUI selects the control as the sibling AFTER the text, so the order is load-bearing.
        var html = UiFloatingLabel.Text("Email")[Span["field"]].ToHtml();

        Assert.StartsWith("<label class=\"floating-label\"><span>Email</span>", html,
            StringComparison.Ordinal);
    }

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

    private static string Filter(string? selected) =>
        UiFilter.Group("tags").Options(["bug", "feature", "docs"]).Selected(selected).ToHtml();
}
