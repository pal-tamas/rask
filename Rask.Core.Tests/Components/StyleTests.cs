using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class StyleTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<style></style>", Style().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<style id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"text/css\" media=\"all\" title=\"main\" nonce=\"abc\"></style>",
            Style(Type: "text/css", Media: "all", Title: "main", Nonce: "abc", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<style>&lt;x&gt;</style>", Style()["<x>"].ToHtml());
}
