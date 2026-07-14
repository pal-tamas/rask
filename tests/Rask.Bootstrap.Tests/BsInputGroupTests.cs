namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsInputGroup (the .input-group wrapper, with Size → input-group-sm/lg)
// and its BsInputGroupText add-on (.input-group-text for a leading/trailing "@", "$", unit, …).
public class BsInputGroupTests
{
    [Fact]
    public void InputGroup_WrapsItems() =>
        Assert.Equal("<div class=\"input-group\"><span class=\"input-group-text\">@</span></div>",
            BsInputGroup()[BsInputGroupText()["@"]].ToHtml());

    [Fact]
    public void InputGroup_Size_AddsSuffixClass() =>
        Assert.Equal(
            "<div class=\"input-group input-group-lg\"><span class=\"input-group-text\">$</span></div>",
            BsInputGroup(Size: BsSize.Lg)[BsInputGroupText()["$"]].ToHtml());

    [Fact]
    public void InputGroup_MergesUserClassAndId() =>
        Assert.StartsWith("<div id=\"ig\" class=\"input-group mb-2\">",
            BsInputGroup(Id: "ig", Class: "mb-2")[BsInputGroupText()["x"]].ToHtml());

    [Fact]
    public void InputGroupText_RendersSpan() =>
        Assert.Equal("<span class=\"input-group-text\">kg</span>", BsInputGroupText()["kg"].ToHtml());
}
