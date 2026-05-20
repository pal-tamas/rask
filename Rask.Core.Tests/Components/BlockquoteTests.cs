namespace Rask.Core.Tests.Components;

public class BlockquoteTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<blockquote></blockquote>", Blockquote().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<blockquote id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\"></blockquote>",
            Blockquote("https://x", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<blockquote>&lt;x&gt;</blockquote>", Blockquote()["<x>"].ToHtml());
}
