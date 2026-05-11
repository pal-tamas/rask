using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ColTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<col />", new Col().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Col.Props(3,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<col id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\" />",
            new Col(props).ToHtml());
    }
}
