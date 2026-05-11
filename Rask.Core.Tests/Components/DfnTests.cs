using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DfnTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dfn></dfn>", new Dfn(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Dfn.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<dfn id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dfn>",
            new Dfn(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dfn>&lt;x&gt;</dfn>", new Dfn(null, "<x>").ToHtml());
}
