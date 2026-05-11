using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class OlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ol></ol>", new Ol(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Ol.Props("1", true, 5,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<ol id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"1\" reversed start=\"5\"></ol>",
            new Ol(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ol>&lt;x&gt;</ol>", new Ol(null, "<x>").ToHtml());
}
