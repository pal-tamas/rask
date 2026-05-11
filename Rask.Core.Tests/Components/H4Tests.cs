using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H4Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h4></h4>", new H4(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new H4.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<h4 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h4>",
            new H4(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h4>&lt;x&gt;</h4>", new H4(null, "<x>").ToHtml());
}
