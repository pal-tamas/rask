using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H6Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h6></h6>", new H6().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<h6 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h6>",
            new H6 { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h6>&lt;x&gt;</h6>", new H6 { Children = ["<x>"] }.ToHtml());
}
