using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class UlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ul></ul>", new Ul().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<ul id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></ul>",
            new Ul { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ul>&lt;x&gt;</ul>", new Ul { Children = ["<x>"] }.ToHtml());
}
