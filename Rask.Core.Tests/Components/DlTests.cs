using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dl></dl>", new Dl(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Dl.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<dl id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dl>",
            new Dl(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dl>&lt;x&gt;</dl>", new Dl(null, "<x>").ToHtml());
}
