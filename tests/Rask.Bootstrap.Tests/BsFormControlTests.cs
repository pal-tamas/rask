#pragma warning disable RASK014 // StubComponent constructed directly in tests

using System.Text.Json;
using Rask.Core;

namespace Rask.Bootstrap.Tests;

public partial class BsFormControlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Input_Controlled_RendersFormControlLabelAndValue()
    {
        var html = BsInput.Value("hi").Label("Name").Id("n").ToHtml();
        Assert.Contains("<label class=\"form-label\" for=\"n\">Name</label>", html);
        Assert.Contains("class=\"form-control\"", html);
        Assert.Contains("value=\"hi\"", html);
    }

    [Fact]
    public void Input_Size_AddsFormControlSize() =>
        Assert.Contains("form-control form-control-lg", BsInput.Value("x").Size(BsSize.Lg).ToHtml());

    [Fact]
    public void Input_HelpText_RendersFormText() =>
        Assert.Contains("<div class=\"form-text\">Hint</div>", BsInput.Value("x").HelpText("Hint").ToHtml());

    [Fact]
    public void Select_Native_RendersFormSelectWithOptions()
    {
        var html = BsSelect.Value("a").Options(["a", "b"]).Native(true).ToHtml();
        Assert.Contains("class=\"form-select\"", html);
        // The selected option keeps its reconciliation key (data-rask-key) so keyed diffing stays stable and
        // the browser's live `selected` property syncs — see SelectTests.BoundSelect_MarkedOption_KeepsItsKey.
        Assert.Contains("data-rask-key=\"0\" value=\"a\" selected", html);
    }

    [Fact]
    public void Textarea_RendersFormControl() =>
        Assert.Contains("class=\"form-control\"", BsTextarea.Value("hi").Rows(3).ToHtml());

    [Fact]
    public void Input_NumericConstraints_ForwardMinMaxStepToCoreInput() =>
        Assert.Contains("min=\"0\" max=\"120\" step=\"1\"",
            BsInput.Value(5).Min("0").Max("120").Step("1").ToHtml());

    [Fact]
    public void Input_DecimalWithoutAnExplicitStep_GetsStepAny() =>
        // BsInput renders through Input<string> with a pre-formatted value, so Input<T>'s own default step
        // never sees the decimal — BsInput has to derive it from its own T. Without it the browser silently
        // refuses to submit a fractional price.
        Assert.Contains("step=\"any\"", BsInput.Value(12.50m).ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Input_IntWithoutAnExplicitStep_KeepsTheWholeNumberConstraint() =>
        Assert.DoesNotContain("step=", BsInput.Value(5).ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Input_TextConstraints_ForwardPatternAndLengths() =>
        Assert.Contains("pattern=\"[a-z]&#x2B;\" maxlength=\"10\" minlength=\"2\"",
            BsInput.Value("x").Pattern("[a-z]+").MaxLength(10).MinLength(2).ToHtml());

    [Fact]
    public void Input_File_ForwardsAcceptCaptureAndMultiple()
    {
        var html = BsInput
            .Value("")
            .Type(InputType.File)
            .Accept(".png,.jpg")
            .Capture("user")
            .Multiple(true)
            .ToHtml();
        Assert.Contains("type=\"file\"", html);
        Assert.Contains("multiple accept=\".png,.jpg\" capture=\"user\"", html);
    }

    [Fact]
    public void Input_ForwardsListAndInputMode()
    {
        var html = BsInput.Value("").List("cities").InputMode("search").ToHtml();
        Assert.Contains("inputmode=\"search\"", html);
        Assert.Contains("list=\"cities\"", html);
    }

    [Fact]
    public void Textarea_ForwardsColsAndLengthConstraints() =>
        Assert.Contains("cols=\"40\" maxlength=\"200\" minlength=\"10\"",
            BsTextarea.Value("hi").Cols(40).MaxLength(200).MinLength(10).ToHtml());

    [Fact]
    public void Check_Switch_RendersFormSwitchAndRole()
    {
        var html = BsCheck.Value(true).Switch(true).Label("On").Id("s").ToHtml();
        Assert.Contains("<div class=\"form-check form-switch\">", html);
        Assert.Contains("class=\"form-check-input\"", html);
        Assert.Contains("role=\"switch\"", html);
        Assert.Contains("<label class=\"form-check-label\" for=\"s\">On</label>", html);
    }

    [Fact]
    public void Input_Required_MarksLabelWithAsterisk()
    {
        var html = BsInput.Value("x").Label("Name").Id("n").Required(true).ToHtml();
        Assert.Contains("Name<span class=\"text-danger ms-1\">*</span>", html);
        Assert.Contains("required", html);
    }

    [Fact]
    public void Input_NotRequired_LabelHasNoAsterisk() =>
        Assert.DoesNotContain("text-danger", BsInput.Value("x").Label("Name").Id("n").ToHtml());

    [Fact]
    public void Field_WrapsControlAndFeedbackInOneContainer()
    {
        // The label/control/feedback live inside a single wrapper <div> so a flex/grid form keeps the
        // .invalid-feedback tight under its input instead of gap-spacing it a row below.
        var html = BsInput.Value("x").Label("Name").Id("n").HelpText("Hint").ToHtml();
        Assert.StartsWith("<div>", html);
        Assert.Contains("<label class=\"form-label\" for=\"n\">Name</label>", html);
        // Help text now carries the id the control's aria-describedby points at.
        Assert.Contains("<div id=\"n-help\" class=\"form-text\">Hint</div>", html);
    }

    [Fact]
    public void Input_HelpText_WiresAriaDescribedbyToHelp()
    {
        // A control with help text is described by it (announced by screen readers) even when valid.
        var html = BsInput.Value("x").Id("n").HelpText("Hint").ToHtml();
        Assert.Contains("aria-describedby=\"n-help\"", html);
        Assert.Contains("<div id=\"n-help\" class=\"form-text\">Hint</div>", html);
        // No error → no aria-invalid.
        Assert.DoesNotContain("aria-invalid", html);
    }

    [Fact]
    public void Input_Valid_EmitsNoAriaInvalidOrDescribedby()
    {
        var html = BsInput.Value("x").Id("n").ToHtml();
        Assert.DoesNotContain("aria-invalid", html);
        Assert.DoesNotContain("aria-describedby", html);
    }

    [Fact]
    public async Task Input_Invalid_WiresAriaInvalidDescribedbyAndAlertFeedback()
    {
        // A bound field that fails validation must expose the failure to assistive tech:
        // aria-invalid on the control, aria-describedby tying it to the error, and the error
        // rendered as a role="alert" live region with the matching id.
        var model = new Model { Name = "" };
        var view = new StubComponent(() => Form.Model(model)[
            BsInput.Bind(() => model.Name)
                .Label("Name")
                .Validate(v => v.Length < 3 ? new[] { "too short" } : Array.Empty<string>())
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit")!;
        using var payload = JsonDocument.Parse("{\"form\":{\"Name\":\"\"}}");
        await view.TryInvokeHandlerAsync(submitId, payload.RootElement);

        var after = view.RenderAsLiveRoot();
        Assert.Contains("is-invalid", after);
        Assert.Contains("aria-invalid=\"true\"", after);
        Assert.Contains("aria-describedby=\"Name-error\"", after);
        Assert.Contains(
            "<div id=\"Name-error\" class=\"invalid-feedback d-block\" role=\"alert\">too short</div>",
            after);
    }

    [Fact]
    public async Task Check_Invalid_WiresAriaInvalidAndAlertFeedback()
    {
        var model = new Terms { Accept = false };
        var view = new StubComponent(() => Form.Model(model)[
            BsCheck
                .Bind(() => model.Accept)
                .Label("Accept")
                .Validate(v => v ? Array.Empty<string>() : new[] { "required" })
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit")!;
        using var payload = JsonDocument.Parse("{\"form\":{\"Accept\":\"\"}}");
        await view.TryInvokeHandlerAsync(submitId, payload.RootElement);

        var after = view.RenderAsLiveRoot();
        Assert.Contains("aria-invalid=\"true\"", after);
        Assert.Contains("aria-describedby=\"Accept-error\"", after);
        Assert.Contains(
            "<div id=\"Accept-error\" class=\"invalid-feedback d-block\" role=\"alert\">required</div>",
            after);
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }

    private sealed class Terms
    {
        public bool Accept { get; set; }
    }
}
