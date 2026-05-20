namespace Rask.Core.Tests.Components;

public class RpTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rp></rp>", Rp().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<rp id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></rp>",
            Rp("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<rp>&lt;x&gt;</rp>", Rp()["<x>"].ToHtml());
}
