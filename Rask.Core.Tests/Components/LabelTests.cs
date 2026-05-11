using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class LabelTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<label></label>", new Label(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Label.Props("name", "f",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<label id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" for=\"name\" form=\"f\"></label>",
            new Label(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<label>&lt;x&gt;</label>", new Label(null, "<x>").ToHtml());
}
