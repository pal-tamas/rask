using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CodeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<code></code>", new Code().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<code id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></code>",
            new Code { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<code>&lt;x&gt;</code>", new Code { Children = ["<x>"] }.ToHtml());
}
