namespace Rask.Core.Tests.Components;

public class LegendTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<legend></legend>", Legend().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<legend id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></legend>",
            Legend("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<legend>&lt;x&gt;</legend>", Legend()["<x>"].ToHtml());
}
