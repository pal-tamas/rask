using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ColgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<colgroup></colgroup>", new Colgroup(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Colgroup.Props(3,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<colgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\"></colgroup>",
            new Colgroup(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<colgroup>&lt;x&gt;</colgroup>", new Colgroup(null, "<x>").ToHtml());
}
