namespace Rask.Core.Tests.Components;

public class SmallTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<small></small>", Small().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<small id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></small>",
            Small("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<small>&lt;x&gt;</small>", Small()["<x>"].ToHtml());
}
