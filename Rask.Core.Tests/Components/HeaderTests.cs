using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HeaderTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<header></header>", new Header(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Header.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<header id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></header>",
            new Header(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<header>&lt;x&gt;</header>", new Header(null, "<x>").ToHtml());
}
