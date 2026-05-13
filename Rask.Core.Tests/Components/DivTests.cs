using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DivTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<div></div>", new Div().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></div>",
            new Div { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() => Assert.Equal("<div>&lt;x&gt;</div>", new Div { Children = ["<x>"] }.ToHtml());
}
