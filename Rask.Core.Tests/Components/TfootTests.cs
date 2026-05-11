using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TfootTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tfoot></tfoot>", new Tfoot(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Tfoot.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<tfoot id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tfoot>",
            new Tfoot(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tfoot>&lt;x&gt;</tfoot>", new Tfoot(null, "<x>").ToHtml());
}
