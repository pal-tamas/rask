namespace Rask.Core.Tests.Components;

public class H2Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h2></h2>", H2().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<h2 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h2>",
            H2("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h2>&lt;x&gt;</h2>", H2()["<x>"].ToHtml());
}
