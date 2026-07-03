namespace Rask.Bootstrap.Tests;

public class BsFormControlTests
{
    [Fact]
    public void Input_Controlled_RendersFormControlLabelAndValue()
    {
        var html = BsInput<string>(Value: "hi", Label: "Name", Id: "n").ToHtml();
        Assert.Contains("<label class=\"form-label\" for=\"n\">Name</label>", html);
        Assert.Contains("class=\"form-control\"", html);
        Assert.Contains("value=\"hi\"", html);
    }

    [Fact]
    public void Input_Size_AddsFormControlSize() =>
        Assert.Contains("form-control form-control-lg", BsInput<string>(Value: "x", Size: BsSize.Lg).ToHtml());

    [Fact]
    public void Input_HelpText_RendersFormText() =>
        Assert.Contains("<div class=\"form-text\">Hint</div>", BsInput<string>(Value: "x", HelpText: "Hint").ToHtml());

    [Fact]
    public void Select_RendersFormSelectWithOptions()
    {
        var html = BsSelect<string>(Value: "a")[Option("a")["A"], Option("b")["B"]].ToHtml();
        Assert.Contains("class=\"form-select\"", html);
        Assert.Contains("<option value=\"a\"", html);
    }

    [Fact]
    public void Textarea_RendersFormControl() =>
        Assert.Contains("class=\"form-control\"", BsTextarea<string>(Value: "hi", Rows: 3).ToHtml());

    [Fact]
    public void Check_Switch_RendersFormSwitchAndRole()
    {
        var html = BsCheck(Value: true, Switch: true, Label: "On", Id: "s").ToHtml();
        Assert.Contains("<div class=\"form-check form-switch\">", html);
        Assert.Contains("class=\"form-check-input\"", html);
        Assert.Contains("role=\"switch\"", html);
        Assert.Contains("<label class=\"form-check-label\" for=\"s\">On</label>", html);
    }

    [Fact]
    public void Input_Required_MarksLabelWithAsterisk()
    {
        var html = BsInput<string>(Value: "x", Label: "Name", Id: "n", Required: true).ToHtml();
        Assert.Contains("Name<span class=\"text-danger ms-1\">*</span>", html);
        Assert.Contains("required", html);
    }

    [Fact]
    public void Input_NotRequired_LabelHasNoAsterisk() =>
        Assert.DoesNotContain("text-danger", BsInput<string>(Value: "x", Label: "Name", Id: "n").ToHtml());

    [Fact]
    public void Field_WrapsControlAndFeedbackInOneContainer()
    {
        // The label/control/feedback live inside a single wrapper <div> so a flex/grid form keeps the
        // .invalid-feedback tight under its input instead of gap-spacing it a row below.
        var html = BsInput<string>(Value: "x", Label: "Name", Id: "n", HelpText: "Hint").ToHtml();
        Assert.StartsWith("<div>", html);
        Assert.Contains("<label class=\"form-label\" for=\"n\">Name</label>", html);
        Assert.Contains("<div class=\"form-text\">Hint</div>", html);
    }
}
