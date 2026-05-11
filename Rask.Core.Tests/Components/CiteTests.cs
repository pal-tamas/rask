using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CiteTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<cite></cite>", new Cite(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Cite.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<cite id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></cite>",
            new Cite(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<cite>&lt;x&gt;</cite>", new Cite(null, "<x>").ToHtml());
}
