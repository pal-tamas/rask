using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H6Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h6></h6>", new H6(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new H6.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<h6 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h6>",
            new H6(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h6>&lt;x&gt;</h6>", new H6(null, "<x>").ToHtml());
}
