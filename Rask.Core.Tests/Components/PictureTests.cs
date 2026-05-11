using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class PictureTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<picture></picture>", new Picture(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Picture.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<picture id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></picture>",
            new Picture(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<picture>&lt;x&gt;</picture>", new Picture(null, "<x>").ToHtml());
}
