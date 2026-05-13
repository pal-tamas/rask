using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HeadTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<head></head>", new Head().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<head id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></head>",
            new Head { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<head>&lt;x&gt;</head>", new Head { Children = ["<x>"] }.ToHtml());
}
