using System.Linq.Expressions;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's field family — field, label, description, error, fieldset, legend — and the field a control
///     draws around itself from its <c>Label</c> and <c>Description</c> props.
/// </summary>
/// <remarks>
///     What these hold is the ASSOCIATION: the label reaches the control, the control names its description
///     and its error, and the error shows what the form says about the bound member. The look is held to
///     Flux's page by <c>FieldParity</c>.
/// </remarks>
public partial class UiFieldTests : global::Rask.Core.RaskMarkup
{
    private sealed class SignUp
    {
        public string Email { get; set; } = "";

        public string Nickname { get; set; } = "";
    }

    [Fact]
    public void A_field_marks_itself_and_stacks_its_parts_by_default()
    {
        var field = Ui.Field[Ui.Label["Email"], Input.Value("").Id("email")];

        var html = field.ToHtml();

        Assert.StartsWith("<div class=\"min-w-0 ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-field", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_field_is_a_grid_with_the_label_beside_the_control()
    {
        var field = Ui.Field.Inline[Input.Value("").Id("terms"), Ui.Label["I agree"]];

        var html = field.ToHtml();

        Assert.StartsWith("<div class=\"grid gap-x-3 gap-y-1.5 ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_in_a_field_points_at_the_control_beside_it()
    {
        var field = Ui.Field[Ui.Label["Email"], Input.Value("").Id("email")];

        var html = field.ToHtml();

        Assert.Contains("<label id=\"email-label\"", html, StringComparison.Ordinal);
        Assert.Contains("for=\"email\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_outside_a_field_names_the_control_it_is_told_to()
    {
        var label = Ui.Label.For("phone")["Phone"];

        var html = label.ToHtml();

        Assert.Contains("for=\"phone\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-label", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" id=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_reaches_a_kit_control_by_the_id_the_control_derives()
    {
        var model = new SignUp();

        var html = Form.Model(model)[
            Ui.Field[Ui.Label["Nickname"], Ui.Input.Bind(() => model.Nickname)]
        ].ToHtml();

        Assert.Contains("for=\"f-nickname\"", html, StringComparison.Ordinal);
        Assert.Contains("<input id=\"f-nickname\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_badge_sits_in_the_label_and_is_kept_out_of_its_name()
    {
        var label = Ui.Label.Badge("Required")["Email"];

        var html = label.ToHtml();

        Assert.Contains("aria-hidden=\"true\">Required</span>", html, StringComparison.Ordinal);
        Assert.Contains("inline-flex", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_slot_is_pushed_to_the_far_end_of_a_full_width_label()
    {
        var label = Ui.Label.Trailing(Span["75%"])["Storage"];

        var html = label.ToHtml();

        Assert.Contains("class=\"flex items-center", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"ms-auto\" data-ui-label-trailing=\"\"><span>75%</span></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_in_a_field_takes_an_id_the_control_can_name()
    {
        var field = Ui.Field[Ui.Label["Email"], Input.Value("").Id("email"), Ui.Description["We never share it."]];

        var html = field.ToHtml();

        Assert.Contains("<div id=\"email-description\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-description=\"\">We never share it.</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_is_a_hidden_live_region_until_the_form_has_a_message()
    {
        var model = new SignUp();
        var ctx = new EditContext(model);
        var page = global::Rask.Testing.Page.Render(() => Form.Model(model).Context(ctx)[
            Ui.Error.For(() => model.Email)
        ]);
        var before = page.Html;

        ctx.AddValidationMessage(new FieldIdentifier(model, nameof(SignUp.Email)), "Enter an email address.");
        var after = page.Render();

        Assert.Contains("class=\"hidden mt-3", before, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\" aria-live=\"polite\" aria-atomic=\"true\"", before, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"hidden", after, StringComparison.Ordinal);
        Assert.Contains("</svg> Enter an email address.</div>", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_error_in_a_field_shows_the_first_message_of_the_bound_control()
    {
        var model = new SignUp();
        var ctx = new EditContext(model);
        ctx.AddValidationMessage(new FieldIdentifier(model, nameof(SignUp.Nickname)), "Taken");
        ctx.AddValidationMessage(new FieldIdentifier(model, nameof(SignUp.Nickname)), "Too short");

        var html = global::Rask.Testing.Page.Render(() => Form.Model(model).Context(ctx)[
            Ui.Field[
                Ui.Label["Nickname"],
                Ui.Input.Bind(() => model.Nickname).ShowValidation(false),
                Ui.Error
            ]
        ]).Html;

        Assert.Contains("<div id=\"f-nickname-error\"", html, StringComparison.Ordinal);
        Assert.Contains("</svg> Taken</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Too short", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_finds_its_messages_by_the_name_of_a_member_of_the_forms_model()
    {
        var model = new SignUp();
        var ctx = new EditContext(model);
        ctx.AddValidationMessage(new FieldIdentifier(model, nameof(SignUp.Email)), "Already registered");

        var html = global::Rask.Testing.Page.Render(() => Form.Model(model).Context(ctx)[
            Ui.Error.Name(nameof(SignUp.Email)),
            Ui.Error.Name(nameof(SignUp.Nickname))
        ]).Html;

        Assert.Contains("Already registered", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "class=\"hidden"));
    }

    [Fact]
    public void A_message_of_your_own_shows_without_a_form()
    {
        var error = Ui.Error.Message("Something went wrong.");

        var html = error.ToHtml();

        Assert.DoesNotContain("hidden mt-3", html, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 20 20\"", html, StringComparison.Ordinal);
        Assert.Contains("Something went wrong.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_icon_of_an_error_can_be_another_one_or_none()
    {
        var without = Ui.Error.Message("No.").Icon(false);
        var other = Ui.Error.Message("No.").Icon(Ui.IconName.InformationCircle);

        var (bare, swapped) = (without.ToHtml(), other.ToHtml());

        Assert.DoesNotContain("<svg", bare, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 20 20\"", swapped, StringComparison.Ordinal);
        Assert.Contains("inline size-5 shrink-0", swapped, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fieldset_is_a_real_fieldset_whose_legend_names_it()
    {
        var fieldset = Ui.Fieldset.Legend("Shipping address").Description("Where the parcel goes.")[Span["fields"]];

        var html = fieldset.ToHtml();

        Assert.StartsWith("<fieldset class=\"m-0 min-w-0 border-0 p-0 ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-legend=\"\">Shipping address</legend>", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-description=\"\">Where the parcel goes.</div><span>fields</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fieldsets_description_is_not_taken_for_that_of_a_field_around_it()
    {
        var field = Ui.Field[Input.Value("").Id("plan"), Ui.Fieldset.Description("Pick one.")[Ui.Legend["Plan"]]];

        var html = field.ToHtml();

        Assert.DoesNotContain("id=\"plan-description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_given_a_label_draws_the_whole_field_around_itself()
    {
        var control = FieldProbe.Label("Email").Badge("Required").Description("Work address.").DescriptionTrailing("We never share it.");

        var html = control.ToHtml();

        Assert.Matches(
            "^<div class=\"min-w-0 [^>]*data-ui-field=\"\"><label id=\"f-email-label\"[^>]*for=\"f-email\">Email<span[^>]*>Required</span></label>"
            + "<div id=\"f-email-description\"[^>]*>Work address.</div><input id=\"f-email\"[^>]*>"
            + "<div id=\"f-email-error\" class=\"hidden [^>]*></div><div id=\"f-email-description-trailing\"[^>]*>We never share it.</div></div>$",
            html);
        Assert.Contains("aria-describedby=\"f-email-description f-email-description-trailing\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-invalid", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_given_no_label_is_the_control_alone()
    {
        var control = FieldProbe;

        var html = control.ToHtml();

        Assert.StartsWith("<input id=\"f-probe\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-field", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_whose_member_has_a_message_is_invalid_and_described_by_it_first()
    {
        var model = new SignUp();
        var ctx = new EditContext(model);
        ctx.AddValidationMessage(new FieldIdentifier(model, nameof(SignUp.Email)), "Enter an email address.");

        var html = global::Rask.Testing.Page.Render(() => Form.Model(model).Context(ctx)[
            FieldProbe.Label("Email").Description("Work address.").Bind(() => model.Email)
        ]).Html;

        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-email-error f-email-description\"", html, StringComparison.Ordinal);
        Assert.Contains("</svg> Enter an email address.</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_control_comes_before_its_label()
    {
        var control = FieldProbe.Label("I agree").Inline(true);

        var html = control.ToHtml();

        Assert.Matches("^<div class=\"grid [^>]*><input id=\"f-i-agree\"[^>]*><label ", html);
    }

    [Fact]
    public void A_control_in_a_field_written_by_hand_names_that_fields_description()
    {
        var field = Ui.Field[Ui.Label["Email"], FieldProbe, Ui.Description["Work address."], Ui.Error];

        var html = field.ToHtml();

        Assert.Contains("for=\"f-probe\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-probe-description\"", html, StringComparison.Ordinal);
    }
}

/// <summary>A control as the kit's are written: three lines of <see cref="UiWithField" /> and its own element.</summary>
internal sealed partial class FieldProbe : Component, IUiFieldControl
{
    public string? Label { get; set; }

    public string? Description { get; set; }

    public string? DescriptionTrailing { get; set; }

    public string? Badge { get; set; }

    public bool? Inline { get; set; }

    public Expression<Func<string>>? Bind { get; set; }

    string IUiFieldControl.ControlId => UiFieldId.Derive(null, Bind, Label ?? "probe");

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this, Label, Description, DescriptionTrailing, Badge);
        var input = Input.Value("").Id(field.ControlId).Data("ui-control", "").Aria(field.Aria);

        return Inline == true ? field.Wrap(input, Ui.FieldVariant.Inline, controlFirst: true) : field.Wrap(input);
    }
}
