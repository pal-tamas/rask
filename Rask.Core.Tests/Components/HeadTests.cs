using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HeadTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<head></head>", Head().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<head id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></head>",
            Head(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<head>&lt;x&gt;</head>", Head()["<x>"].ToHtml());
}
