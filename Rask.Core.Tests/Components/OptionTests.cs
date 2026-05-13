using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class OptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<option></option>", new Option().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<option id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"v\" selected disabled label=\"L\"></option>",
            new Option { Value = "v", Selected = true, Disabled = true, Label = "L", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<option>&lt;x&gt;</option>", new Option { Children = ["<x>"] }.ToHtml());
}
