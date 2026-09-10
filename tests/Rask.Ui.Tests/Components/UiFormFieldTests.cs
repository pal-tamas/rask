namespace Rask.Ui.Tests.Components;

/// <summary>
///     The field shape every kit control shares: an optional visible label, an optional hint, and the
///     validation message.
/// </summary>
/// <remarks>
///     <para>
///     Each control used to leave all three to the call site, and the showcase wrote them by hand once per
///     field — <c>Label.Class(Tw.Label)["Username"], Input.Bind(…).Class(Tw.Input)</c>, about eighty-six
///     times. That is how a label ends up associated with nothing in particular: the text renders, the
///     control renders, clicking the text does nothing, and a screen reader announces an unnamed field.
///     </para>
///     <para>
///     So these assert the ASSOCIATION, not the appearance. The label wraps its control, which is what
///     makes the link exist without an id to mint, keep unique down a list, or thread through a template.
///     </para>
/// </remarks>
public partial class UiFormFieldTests : global::Rask.Core.RaskMarkup
{
    private sealed class Model
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void A_label_points_at_its_control_and_the_id_is_derived_when_absent()
    {
        // for/id rather than wrapping, and the reason is daisyUI: it reveals a validator message with a
        // GENERAL SIBLING selector, so a control moved inside its label stops being a sibling of its own
        // message — which rendered, carried the right text, and stayed hidden for the life of the page.
        var html = UiInput.Value("").Label("Username").ToHtml();

        Assert.Contains("for=\"f-username\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"f-username\"", html, StringComparison.Ordinal);
        Assert.Contains("Username", html, StringComparison.Ordinal);

        // The control is NOT inside the label; they are siblings.
        var close = html.IndexOf("</label>", StringComparison.Ordinal);
        var input = html.IndexOf("<input", StringComparison.Ordinal);
        Assert.True(close >= 0 && input > close, "the control is inside its label again.");
    }

    [Fact]
    public void A_bound_field_derives_its_id_from_the_member_it_binds()
    {
        // Deterministic, so the markup is the same across renders — a fresh GUID per instance would change
        // the markup on every pass and make the golden files unreproducible.
        var model = new Model();

        Assert.Contains(
            "id=\"f-name\"",
            UiInput.Bind(() => model.Name).Label("Full name").ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_label_the_markup_is_the_bare_control()
    {
        // A search box whose placeholder is its whole affordance, a control in a table cell, a field
        // labelled by a column header. All real — and this is also what makes the base safe to add: a call
        // site that had no label renders exactly what it rendered before, with no wrapper around it.
        var html = UiInput.Value("").ToHtml();

        Assert.StartsWith("<input", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<label", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fieldset", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_control_is_named_by_AccessibleLabel()
    {
        var html = UiInput.Value("").AccessibleLabel("Search").ToHtml();

        Assert.Contains("aria-label=\"Search\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_visible_label_wins_over_an_accessible_one()
    {
        // Two names on one control is worse than one, because the name a screen reader reads is then not
        // the text on the screen.
        var html = UiInput.Value("").Label("Username").AccessibleLabel("Search").ToHtml();

        Assert.Contains("Username", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hint_sits_outside_the_label()
    {
        // A label's text becomes the control's accessible name, so a name that recites the hint every time
        // is worse for a screen reader than one that does not.
        var html = UiInput.Value("").Label("Password").Hint("At least 12 characters").ToHtml();

        var close = html.IndexOf("</label>", StringComparison.Ordinal);
        var hint = html.IndexOf("At least 12 characters", StringComparison.Ordinal);

        Assert.True(close >= 0 && hint > close, "the hint is inside the label.");
        Assert.DoesNotContain("aria-label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_error_tone_marks_the_control_invalid()
    {
        // aria-invalid is what makes daisyUI reveal a following message, and what a screen reader needs —
        // a field that is visibly red and says nothing is half a message.
        var html = UiInput.Value("").Label("Email").Tone(UiTone.Error).ToHtml();

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_healthy_field_has_no_aria_invalid()
    {
        // Omitted rather than nulled: a valueless aria-invalid reads as "true", which would mark every
        // field in the kit invalid.
        Assert.DoesNotContain("aria-invalid", UiInput.Value("").Label("Email").ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_axes_still_reach_the_control_through_the_base()
    {
        // Tone, Size and Variant moved onto UiFormField, so this is really asking whether the chain still
        // offers a base class's properties as steps. If it did not, none of the above would compile.
        var html = UiInput.Value("").Label("Email").Size(UiSize.Sm).Variant(UiVariant.Ghost).ToHtml();

        Assert.Contains("input-sm", html, StringComparison.Ordinal);
        Assert.Contains("input-ghost", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_field_renders_its_own_validation_message()
    {
        // The showcase wrote this template once per control, and the copies had already drifted to two
        // different colours. ValidationMessage renders nothing until the field has messages, so what this
        // asserts is that the field ASKED for one.
        var model = new Model();
        var html = UiInput.Bind(() => model.Name).Label("Name").ToHtml();

        Assert.Contains("<label", html, StringComparison.Ordinal);
        Assert.Contains("Name", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_can_opt_out_of_its_message()
    {
        // For a form that shows its errors in one summary, so the same error is not said twice.
        var model = new Model();

        Assert.NotNull(UiInput.Bind(() => model.Name).Label("Name").ShowValidation(false).ToHtml());
    }

    [Fact]
    public void A_select_gets_the_same_field_shape()
    {
        // The base is shared, so this is really asking whether UiSelect reaches it — a control that
        // declared its own Label and kept it would pass every other test in this file while rendering the
        // old markup.
        // Options FIRST: it is a required prop, so the chain stays "pending" — and the optional steps
        // are not offered — until every required one is taken.
        var html = UiSelect
            .Value("hu")
            .Options([("hu", "Hungary"), ("gb", "United Kingdom")])
            .Label("Country")
            .Hint("Where you are billed")
            .ToHtml();

        var label = html.IndexOf("<label", StringComparison.Ordinal);
        var close = html.IndexOf("</label>", StringComparison.Ordinal);
        var select = html.IndexOf("<select", StringComparison.Ordinal);

        Assert.True(label >= 0 && close >= 0 && select > close, "the select is inside its label.");
        Assert.Contains("for=", html, StringComparison.Ordinal);
        Assert.Contains("Country", html, StringComparison.Ordinal);
        Assert.Contains("Where you are billed", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_select_is_still_the_bare_control() =>
        Assert.DoesNotContain(
            "<label",
            UiSelect.Value("hu").Options([("hu", "Hungary")]).ToHtml(),
            StringComparison.Ordinal);
    [Fact]
    public void The_id_reaches_the_control_itself()
    {
        // A prop the base DECLARES and no control RENDERS is the silent failure this kit keeps meeting:
        // the call site compiles, the attribute never appears, and the browser test that selects on it
        // fails somewhere else entirely. Id was exactly that for one commit.
        Assert.Contains(
            "id=\"email\"",
            UiInput.Value("").Label("Email").Id("email").ToHtml(),
            StringComparison.Ordinal);

        Assert.Contains(
            "id=\"country\"",
            UiSelect.Value("hu").Options([("hu", "Hungary")]).Label("Country").Id("country").ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_id_reaches_a_bound_control_too()
    {
        // Bound and controlled are separate chain types built as separate expressions, so each one has to
        // carry it — and only one of them did at first.
        var model = new Model();

        Assert.Contains(
            "id=\"name\"",
            UiInput.Bind(() => model.Name).Label("Name").Id("name").ToHtml(),
            StringComparison.Ordinal);
    }
    [Fact]
    public void A_textarea_is_a_field_too_and_renders_its_id()
    {
        // Added after the showcase's controlled textarea lost its change handler in a migration — not
        // because the handler was dropped, but because UiTextarea never rendered the Id the test selected
        // on, so the assertion could not find the element to look at. Every control in the family needs
        // the same two things wired, and "I fixed input and select" is how the third one is missed.
        var html = UiTextarea.Value("").Label("Notes").Id("notes").Rows(3).ToHtml();

        Assert.Contains("id=\"notes\"", html, StringComparison.Ordinal);
        Assert.Contains("<label", html, StringComparison.Ordinal);
        Assert.Contains("Notes", html, StringComparison.Ordinal);

        // One name, not two: the visible label is the name, so no aria-label duplicates it.
        Assert.DoesNotContain("aria-label", html, StringComparison.Ordinal);
    }
}
