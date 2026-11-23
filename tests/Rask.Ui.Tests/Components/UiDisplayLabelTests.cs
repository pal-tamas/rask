using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     As in Flux, a bound kit control draws a label only when it is given one: its member's <c>[Display(Name = …)]</c>
///     labels it from a field — a <c>Ui.Field</c> with no <c>Ui.Label</c>, or <c>Ui.Field.Bind</c> — and a
///     <c>[DisplayName]</c> never does.
/// </summary>
public partial class UiDisplayLabelTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_bare_bound_input_draws_no_label_of_its_members()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Email)].ToHtml();

        Assert.DoesNotContain("<label", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Work email", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_around_a_bound_input_is_labelled_by_its_members_display_name()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Field[Ui.Input.Bind(() => model.Email)]].ToHtml();

        Assert.Contains(">Work email</label>", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-email\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_display_name_attribute_labels_nothing()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Field[Ui.Input.Bind(() => model.Phone)]].ToHtml();

        Assert.DoesNotContain("<label", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Telephone", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_the_call_site_gives_is_drawn()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Email).Label("Email")].ToHtml();

        Assert.Contains(">Email</label>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Work email", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_the_call_site_composes_keeps_its_one_label()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Field[Ui.Label["Address"], Ui.Input.Bind(() => model.Email)]].ToHtml();

        Assert.Single(Regex.Matches(html, "<label"));
        Assert.DoesNotContain("Work email", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_around_a_member_with_no_attribute_draws_no_label()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Field[Ui.Input.Bind(() => model.Plain)]].ToHtml();

        Assert.DoesNotContain("<label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_bound_checkbox_draws_no_label_and_a_field_labels_it_beside_itself()
    {
        var model = new Account();

        var bare = Form.Model(model)[Ui.Checkbox.Bind(() => model.Terms)].ToHtml();
        var field = Form.Model(model)[Ui.Field[Ui.Checkbox.Bind(() => model.Terms)]].ToHtml();

        Assert.DoesNotContain("f-terms-label", bare, StringComparison.Ordinal);
        Assert.Contains(">Accept the terms</label>", field, StringComparison.Ordinal);
    }

    [Fact]
    public void A_composer_in_a_field_draws_its_display_name_once()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Field[Ui.Composer.Bind(() => model.Notes)]].ToHtml();

        Assert.Single(Regex.Matches(html, ">Remarks</label>"));
    }

    private sealed class Account
    {
        [Display(Name = "Work email")] public string Email { get; set; } = "";

        [Display(Name = "Remarks")] public string Notes { get; set; } = "";

#pragma warning disable RASK131 // The [DisplayName] Rask does not read is what this member is here to prove.
        [DisplayName("Telephone")] public string Phone { get; set; } = "";
#pragma warning restore RASK131

        [Display(Name = "Accept the terms")] public bool Terms { get; set; }

        public string Plain { get; set; } = "";
    }
}
