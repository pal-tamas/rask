using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DtTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dt></dt>", new Dt(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Dt.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<dt id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dt>",
            new Dt(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dt>&lt;x&gt;</dt>", new Dt(null, "<x>").ToHtml());
}
