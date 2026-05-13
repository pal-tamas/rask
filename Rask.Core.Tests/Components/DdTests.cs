using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DdTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dd></dd>", new Dd().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<dd id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dd>",
            new Dd { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dd>&lt;x&gt;</dd>", new Dd { Children = ["<x>"] }.ToHtml());
}
