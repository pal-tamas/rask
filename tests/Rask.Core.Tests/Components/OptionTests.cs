namespace Rask.Core.Tests.Components;

public class OptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<option></option>", Option().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<option id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"v\" selected disabled label=\"L\"></option>",
            Option("v", true, true, "L", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<option>&lt;x&gt;</option>", Option()["<x>"].ToHtml());
}
