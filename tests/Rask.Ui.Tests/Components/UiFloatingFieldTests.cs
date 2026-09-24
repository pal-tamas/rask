using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     A labelled text input, textarea or native select floats its label by default.
/// </summary>
/// <remarks>
///     daisyUI's floating label is a <c>&lt;label class="floating-label"&gt;</c> holding the caption
///     <c>&lt;span&gt;</c> FIRST and the control after it, and it raises the caption once the control stops
///     showing its placeholder — so the order and the placeholder are both part of the contract here.
/// </remarks>
public partial class UiFloatingFieldTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_labelled_input_floats_its_label_by_default()
    {
        var html = Ui.Input.Value("").Label("Email").ToHtml();

        Assert.StartsWith(
            "<label class=\"floating-label\" for=\"f-email\"><span id=\"f-email-label\">Email</span><input ",
            html,
            StringComparison.Ordinal);
        Assert.Contains("id=\"f-email\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fieldset-legend", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_floating_field_is_named_by_its_caption_alone()
    {
        // The label wraps the control, so a name computed from the label is the caption AND the control inside it,
        // whose empty value falls back to the placeholder the caption needs — Chromium announced "Email Email".
        var html = Ui.Input.Value("").Label("Email").ToHtml();

        Assert.Contains("aria-labelledby=\"f-email-label\"", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"f-email-label\">", html, StringComparison.Ordinal);
        // A legend does not wrap its control, so its plain for/id is right and nothing extra is written.
        Assert.DoesNotContain("aria-labelledby", Ui.Input.Value("").Label("Email").Floating(false).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_floating_field_uses_its_label_as_the_placeholder_when_none_is_given()
    {
        // An empty placeholder never counts as shown, so daisyUI would leave the caption risen over an empty
        // box. The label text is the placeholder daisyUI's own examples use.
        Assert.Contains("placeholder=\"Email\"", Ui.Input.Value("").Label("Email").ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_floating_label_is_the_placeholder_even_when_the_call_site_gave_one()
    {
        // A different placeholder would sit in the box in the label's place and hide the one thing saying what
        // the field is for until somebody focused it. Guidance about the value belongs in Hint.
        var html = Ui.Input.Value("").Label("Email").Placeholder("you@example.com").ToHtml();

        Assert.Contains("placeholder=\"Email\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("you@example.com", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_placeholder_still_applies_where_no_label_floats()
    {
        Assert.Contains(
            "placeholder=\"you@example.com\"",
            Ui.Input.Value("").Label("Email").Floating(false).Placeholder("you@example.com").ToHtml(),
            StringComparison.Ordinal);

        Assert.Contains(
            "placeholder=\"Search guides\"",
            Ui.Input.Value("").AccessibleLabel("Search").Placeholder("Search guides").ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Floating_false_puts_the_label_above_the_field_as_a_legend()
    {
        var html = Ui.Input.Value("").Label("Email").Floating(false).ToHtml();

        Assert.StartsWith("<div class=\"fieldset\"><label class=\"fieldset-legend\" for=\"f-email\">Email</label><input ",
            html, StringComparison.Ordinal);
        Assert.DoesNotContain("floating-label", html, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder=\"Email\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_with_no_label_is_the_bare_control() =>
        Assert.StartsWith("<input ", Ui.Input.Value("").AccessibleLabel("Search").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_hint_puts_the_floating_label_in_a_fieldset_with_the_hint_under_it()
    {
        var html = Ui.Input.Value("").Label("Email").Hint("We never share it.").ToHtml();

        Assert.StartsWith("<div class=\"fieldset\"><label class=\"floating-label\"", html, StringComparison.Ordinal);
        Assert.Contains("</label><p id=\"f-email-hint\" class=\"label\">We never share it.</p></div>", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_hint_is_a_sibling_of_the_floating_label()
    {
        var html = Ui.Input.Value("x").Label("Email").Tone(Ui.Tone.Error).Error("Enter an email.").ToHtml();

        Assert.Contains("</label><p id=\"f-email-error\" class=\"validator-hint\">Enter an email.</p>", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_kit_stylesheet_reveals_an_error_hint_through_the_floating_label()
    {
        // daisyUI reveals .validator-hint with a SIBLING selector from the control, and the control is inside
        // the label now. Without the kit's own rule the message renders, carries the right text, and stays
        // `visibility: hidden` — the failure a browser test caught once already for the wrapping legend.
        Assert.Matches(new Regex(@"\.floating-label:has\([^{]*\)\s*~\s*\.validator-hint\s*\{[^}]*visibility:\s*visible"),
            UiStylesheet.Css);
    }

    [Fact]
    public void A_labelled_textarea_floats_its_label_by_default()
    {
        var html = Ui.Textarea.Value("").Label("Notes").ToHtml();

        Assert.StartsWith("<label class=\"floating-label\" for=\"f-notes\"><span id=\"f-notes-label\">Notes</span><textarea ", html,
            StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Notes\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_native_select_floats_its_label_by_default()
    {
        var html = Ui.Select.Value("a").Options([("a", "A"), ("b", "B")]).Label("Plan").ToHtml();

        Assert.StartsWith("<label class=\"floating-label\" for=\"f-plan\"><span id=\"f-plan-label\">Plan</span><select ", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_drawn_select_keeps_the_legend_because_there_is_no_select_to_float_over()
    {
        var html = Ui.Select.Value("a").Options([("a", "A"), ("b", "B")]).Label("Plan").Native(false).ToHtml();

        Assert.Contains("fieldset-legend", html, StringComparison.Ordinal);
        Assert.DoesNotContain("floating-label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_with_its_own_layout_is_untouched_by_floating()
    {
        // Only the text input, the textarea and the native select take the shared field layout; a range,
        // a checkbox or a rating draws its own, and a floating caption inside a box that holds no text
        // would mean nothing.
        Assert.DoesNotContain("floating-label", Ui.Range.Value(0.5).Label("Volume").ToHtml(), StringComparison.Ordinal);
    }
}
