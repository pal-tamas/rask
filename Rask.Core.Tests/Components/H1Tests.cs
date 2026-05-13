using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H1Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h1></h1>", new H1().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<h1 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h1>",
            new H1 { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h1>&lt;x&gt;</h1>", new H1 { Children = ["<x>"] }.ToHtml());
}
