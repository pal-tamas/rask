using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class EmTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<em></em>", new Em().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<em id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></em>",
            new Em { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<em>&lt;x&gt;</em>", new Em { Children = ["<x>"] }.ToHtml());
}
