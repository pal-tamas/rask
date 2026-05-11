using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AsideTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<aside></aside>", new Aside(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Aside.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<aside id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></aside>",
            new Aside(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<aside>&lt;x&gt;</aside>", new Aside(null, "<x>").ToHtml());
}
