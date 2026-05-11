using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class StyleTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<style></style>", new Style(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Style.Props(
            "text/css",
            "all",
            "main",
            "abc",
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<style id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"text/css\" media=\"all\" title=\"main\" nonce=\"abc\"></style>",
            new Style(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<style>&lt;x&gt;</style>", new Style(null, "<x>").ToHtml());
}
