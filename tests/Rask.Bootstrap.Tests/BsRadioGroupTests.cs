#pragma warning disable RASK014 // StubComponent constructed directly in tests

using System.Text.Json;

namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsRadioGroup. Each option is a <div class="form-check"> holding a
// labelable radio (<input type="radio">) whose <label for> associates by id. A group Label wraps the
// set in a <fieldset> named by a <legend> (the accessible name for the group); without it the bare
// per-item fragment is kept. Validation surfaces as a role="alert" live region the radios point at
// via aria-describedby, with aria-invalid on each input.
public class BsRadioGroupTests
{
    [Fact]
    public void RadioGroup_Controlled_RendersRadiosWithPerItemLabelAssociation()
    {
        var html = BsRadioGroup(Options: ["Free", "Pro"], Value: "Free", Name: "plan", OnChange: _ => { }).ToHtml();
        // No group Label ⇒ bare fragment, no fieldset/legend wrapper.
        Assert.DoesNotContain("<fieldset", html);
        Assert.DoesNotContain("<legend", html);
        Assert.StartsWith("<div class=\"form-check\" data-rask-key=\"0\">", html);
        Assert.Contains(
            "<input id=\"plan-0\" class=\"form-check-input\" type=\"radio\" name=\"plan\" value=\"Free\" checked />",
            html);
        Assert.Contains("<label class=\"form-check-label\" for=\"plan-0\">Free</label>", html);
        Assert.Contains("<input id=\"plan-1\" class=\"form-check-input\" type=\"radio\" name=\"plan\" value=\"Pro\" />",
            html);
    }

    [Fact]
    public void RadioGroup_WithLabel_WrapsInFieldsetNamedByLegend()
    {
        var html = BsRadioGroup(Options: ["Free", "Pro"], Value: "Free", Name: "plan", Label: "Plan",
            OnChange: _ => { }).ToHtml();
        // A set of related radios becomes a <fieldset> whose <legend> is the group's accessible name.
        Assert.StartsWith("<fieldset class=\"border-0 p-0 m-0\">", html);
        Assert.Contains("<legend class=\"form-label fs-6\">Plan</legend>", html);
        Assert.EndsWith("</fieldset>", html);
        Assert.Contains("<label class=\"form-check-label\" for=\"plan-0\">Free</label>", html);
    }

    [Fact]
    public void RadioGroup_Disabled_DisablesEveryRadio()
    {
        var html = BsRadioGroup(Options: ["Free", "Pro"], Value: "Free", Name: "plan", Disabled: true,
            OnChange: _ => { }).ToHtml();
        Assert.Contains("value=\"Free\" disabled checked />", html);
        Assert.Contains("value=\"Pro\" disabled />", html);
    }

    [Fact]
    public void RadioGroup_OptionLabel_RendersRichLabels() =>
        Assert.Contains("<label class=\"form-check-label\" for=\"plan-0\"><strong>Free</strong></label>",
            BsRadioGroup(Options: ["Free"], Value: "Free", Name: "plan",
                OptionLabel: p => Strong()[p], OnChange: _ => { }).ToHtml());

    [Fact]
    public async Task RadioGroup_Bound_Invalid_WiresAriaInvalidDescribedbyAndAlertFeedback()
    {
        // A bound radio group that fails validation must expose the failure to assistive tech: aria-invalid
        // + aria-describedby on each radio, and the error as a role="alert" live region with the matching id.
        var model = new PlanModel();
        var view = new StubComponent(() => Form(model)[
            BsRadioGroup(() => model.Plan, ["Free", "Pro"], Label: "Plan",
                Validate: v => string.IsNullOrEmpty(v) ? new[] { "pick a plan" } : Array.Empty<string>())
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit")!;
        using var payload = JsonDocument.Parse("{\"form\":{}}");
        await view.TryInvokeHandlerAsync(submitId, payload.RootElement);

        var after = view.RenderAsLiveRoot();
        Assert.Contains("aria-invalid=\"true\" aria-describedby=\"Plan-error\"", after);
        Assert.Contains(
            "<div id=\"Plan-error\" class=\"invalid-feedback d-block\" role=\"alert\">pick a plan</div>",
            after);
    }

    [Fact]
    public void RadioGroup_TwoUnnamedControlled_GetDistinctGroupNames()
    {
        // Two id-less controlled groups must not both fall back to name="radio-group": the browser would
        // treat them as ONE radio group (selecting in one clears the other) and their ids would collide.
        var nameA = Markup.Attr(BsRadioGroup(Options: ["x"], Value: "x", OnChange: _ => { }).ToHtml(), "name");
        var nameB = Markup.Attr(BsRadioGroup(Options: ["x"], Value: "x", OnChange: _ => { }).ToHtml(), "name");
        Assert.StartsWith("radio-group-", nameA);
        Assert.NotEqual(nameA, nameB);
    }

    [Fact]
    public void RadioGroup_NeitherBindNorHandler_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            BsRadioGroup(Options: ["a"], Value: "a").ToHtml());

    private sealed class PlanModel
    {
        public string Plan { get; set; } = "";
    }
}
