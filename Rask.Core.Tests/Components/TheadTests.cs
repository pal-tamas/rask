using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TheadTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<thead></thead>", new Thead(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Thead.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<thead id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></thead>",
            new Thead(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<thead>&lt;x&gt;</thead>", new Thead(null, "<x>").ToHtml());
}
