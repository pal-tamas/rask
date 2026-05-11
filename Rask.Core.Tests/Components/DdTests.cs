using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DdTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dd></dd>", new Dd(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Dd.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<dd id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dd>",
            new Dd(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dd>&lt;x&gt;</dd>", new Dd(null, "<x>").ToHtml());
}
