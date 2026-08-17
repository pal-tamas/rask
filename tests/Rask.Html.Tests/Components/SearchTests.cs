namespace Rask.Html.Tests.Components;

public partial class SearchTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<search></search>", Search.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<search id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></search>",
            Search.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<search>&lt;x&gt;</search>", Search["<x>"].ToHtml());
}
