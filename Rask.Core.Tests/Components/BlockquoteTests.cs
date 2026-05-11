using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BlockquoteTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<blockquote></blockquote>", new Blockquote(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Blockquote.Props("https://x", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<blockquote id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\"></blockquote>",
            new Blockquote(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<blockquote>&lt;x&gt;</blockquote>", new Blockquote(null, "<x>").ToHtml());
}
