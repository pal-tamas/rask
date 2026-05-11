using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DetailsTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<details></details>", new Details(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Details.Props(true,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<details id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" open></details>",
            new Details(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<details>&lt;x&gt;</details>", new Details(null, "<x>").ToHtml());
}
