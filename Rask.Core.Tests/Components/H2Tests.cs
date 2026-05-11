using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H2Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h2></h2>", new H2(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new H2.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<h2 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h2>",
            new H2(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h2>&lt;x&gt;</h2>", new H2(null, "<x>").ToHtml());
}
