using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TfootTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tfoot></tfoot>", new Tfoot().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<tfoot id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tfoot>",
            new Tfoot { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tfoot>&lt;x&gt;</tfoot>", new Tfoot { Children = ["<x>"] }.ToHtml());
}
