namespace Rask.Core.Tests.Components;

public class EmTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<em></em>", Em().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<em id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></em>",
            Em("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<em>&lt;x&gt;</em>", Em()["<x>"].ToHtml());
}
