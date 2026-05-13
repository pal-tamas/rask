using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class UTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<u></u>", new U().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<u id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></u>",
            new U { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<u>&lt;x&gt;</u>", new U { Children = ["<x>"] }.ToHtml());
}
