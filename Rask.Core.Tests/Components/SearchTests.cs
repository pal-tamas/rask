namespace Rask.Core.Tests.Components;

public class SearchTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<search></search>", Search().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<search id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></search>",
            Search("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<search>&lt;x&gt;</search>", Search()["<x>"].ToHtml());
}
