using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     A bound kit control given no <c>Label</c> is labelled by its member's <c>[Display(Name = …)]</c> or
///     <c>[DisplayName]</c>, <c>[Display]</c> first, as Blazor's <c>Label</c> reads them; a label the call site gives,
///     or a <c>Ui.Field</c> it composes, keeps the say.
/// </summary>
public partial class UiDisplayNameLabelTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_bound_input_is_labelled_by_its_members_display_name()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Email)].ToHtml();

        Assert.Contains(">Work email</label>", html, StringComparison.Ordinal);
        Assert.Contains("for=\"f-email\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_display_name_attribute_labels_a_bound_control_too()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Textarea.Bind(() => model.Notes)].ToHtml();

        Assert.Contains(">Remarks</label>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_wins_over_display_name_for_the_label()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Phone)].ToHtml();

        Assert.Contains(">Mobile</label>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Telephone", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_the_call_site_gives_wins_over_the_attribute()
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
    public void A_member_with_neither_attribute_draws_no_label()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Plain)].ToHtml();

        Assert.DoesNotContain("<label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_checkbox_is_labelled_beside_itself_by_its_display_name()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Checkbox.Bind(() => model.Terms)].ToHtml();

        Assert.Contains(">Accept the terms</label>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_composer_draws_its_display_name_once_and_its_box_none_of_its_own()
    {
        var model = new Account();

        var html = Form.Model(model)[Ui.Composer.Bind(() => model.Notes)].ToHtml();

        Assert.Single(Regex.Matches(html, ">Remarks</label>"));
    }

    private sealed class Account
    {
        [Display(Name = "Work email")] public string Email { get; set; } = "";

        [DisplayName("Remarks")] public string Notes { get; set; } = "";

        [Display(Name = "Mobile"), DisplayName("Telephone")] public string Phone { get; set; } = "";

        [Display(Name = "Accept the terms")] public bool Terms { get; set; }

        public string Plain { get; set; } = "";
    }
}
