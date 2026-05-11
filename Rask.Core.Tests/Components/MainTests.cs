using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MainTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<main></main>", new Main(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Main.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<main id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></main>",
            new Main(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<main>&lt;x&gt;</main>", new Main(null, "<x>").ToHtml());
}
