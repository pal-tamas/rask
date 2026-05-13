using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ColgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<colgroup></colgroup>", new Colgroup().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<colgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\"></colgroup>",
            new Colgroup { Span = 3, Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<colgroup>&lt;x&gt;</colgroup>", new Colgroup { Children = ["<x>"] }.ToHtml());
}
