namespace Rask.Core.Tests.Components;

public class SampTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<samp></samp>", Samp().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<samp id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></samp>",
            Samp("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<samp>&lt;x&gt;</samp>", Samp()["<x>"].ToHtml());
}
