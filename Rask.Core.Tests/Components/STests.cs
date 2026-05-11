using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class STests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<s></s>", new S(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new S.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<s id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></s>",
            new S(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<s>&lt;x&gt;</s>", new S(null, "<x>").ToHtml());
}
