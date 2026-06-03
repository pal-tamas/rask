namespace Rask.Core.Tests.Components;

public class MainTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<main></main>", Main().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<main id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></main>",
            Main("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<main>&lt;x&gt;</main>", Main()["<x>"].ToHtml());
}
