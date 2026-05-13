using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H2Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h2></h2>", new H2().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<h2 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h2>",
            new H2 { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h2>&lt;x&gt;</h2>", new H2 { Children = ["<x>"] }.ToHtml());
}
