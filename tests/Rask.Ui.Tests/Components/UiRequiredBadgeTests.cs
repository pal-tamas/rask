using System.ComponentModel.DataAnnotations;
using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     A labelled kit control whose field must be filled says so beside its label, as Flux's badge does — from the same
///     answer its aria-required comes from — unless the call site gives its own badge, or an empty one.
/// </summary>
public partial class UiRequiredBadgeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_required_field_says_required_beside_its_label()
    {
        var model = new Signup();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Email).Label("Email")].ToHtml();

        Assert.Contains(">Required</span>", html, StringComparison.Ordinal);
        Assert.Contains("aria-required=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_optional_field_has_no_badge()
    {
        var model = new Signup();

        var html = Form.Model(model)[Ui.Input.Bind(() => model.Nickname).Label("Nickname")].ToHtml();

        Assert.DoesNotContain("Required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_badge_the_call_site_gives_wins_and_an_empty_one_says_nothing()
    {
        var model = new Signup();

        var own = Form.Model(model)[Ui.Textarea.Bind(() => model.Email).Label("Email").Badge("Unique")].ToHtml();
        var none = Form.Model(model)[Ui.Textarea.Bind(() => model.Email).Label("Email").Badge("")].ToHtml();

        Assert.Contains(">Unique</span>", own, StringComparison.Ordinal);
        Assert.DoesNotContain("Required", own, StringComparison.Ordinal);
        Assert.DoesNotContain("Required", none, StringComparison.Ordinal);
    }

    [Fact]
    public void The_badge_is_worded_in_the_readers_language()
    {
        var model = new Signup();

        var html = KitCulture.In("hu", () => Form.Model(model)[Ui.Input.Bind(() => model.Email).Label("E-mail")].ToHtml());

        Assert.Contains(">Kötelező</span>", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_labelled_by_its_display_name_gets_the_badge_too()
    {
        var model = new Signup();

        var html = Form.Model(model)[Ui.Field[Ui.Input.Bind(() => model.Email)]].ToHtml();

        Assert.Contains(">Work email", html, StringComparison.Ordinal);
        Assert.Contains(">Required</span>", html, StringComparison.Ordinal);
    }

    private sealed class Signup
    {
        [Required, Display(Name = "Work email")] public string Email { get; set; } = "";

        public string? Nickname { get; set; }
    }
}
