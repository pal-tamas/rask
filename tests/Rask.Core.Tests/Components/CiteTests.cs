namespace Rask.Core.Tests.Components;

public partial class CiteTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<cite></cite>", Cite.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<cite id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></cite>",
            Cite.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<cite>&lt;x&gt;</cite>", Cite["<x>"].ToHtml());
}
