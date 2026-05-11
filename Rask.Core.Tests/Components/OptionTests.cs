using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class OptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<option></option>", new Option(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Option.Props("v", true, true, "L",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<option id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"v\" selected disabled label=\"L\"></option>",
            new Option(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<option>&lt;x&gt;</option>", new Option(null, "<x>").ToHtml());
}
