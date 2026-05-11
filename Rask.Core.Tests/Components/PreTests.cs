using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class PreTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<pre></pre>", new Pre(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Pre.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<pre id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></pre>",
            new Pre(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<pre>&lt;x&gt;</pre>", new Pre(null, "<x>").ToHtml());
}
