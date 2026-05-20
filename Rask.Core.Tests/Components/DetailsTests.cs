namespace Rask.Core.Tests.Components;

public class DetailsTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<details></details>", Details().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<details id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" open></details>",
            Details(true, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<details>&lt;x&gt;</details>", Details()["<x>"].ToHtml());
}
