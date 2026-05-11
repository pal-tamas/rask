using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H5Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h5></h5>", new H5(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new H5.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<h5 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h5>",
            new H5(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h5>&lt;x&gt;</h5>", new H5(null, "<x>").ToHtml());
}
