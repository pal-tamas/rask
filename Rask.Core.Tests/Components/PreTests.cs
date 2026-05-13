using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class PreTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<pre></pre>", new Pre().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<pre id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></pre>",
            new Pre { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<pre>&lt;x&gt;</pre>", new Pre { Children = ["<x>"] }.ToHtml());
}
