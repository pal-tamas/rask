namespace Rask.Core.Tests.Components;

public class H3Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h3></h3>", H3().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<h3 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h3>",
            H3("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h3>&lt;x&gt;</h3>", H3()["<x>"].ToHtml());
}
