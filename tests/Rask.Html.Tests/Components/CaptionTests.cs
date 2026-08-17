namespace Rask.Html.Tests.Components;

public partial class CaptionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<caption></caption>", Caption.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<caption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></caption>",
            Caption.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<caption>&lt;x&gt;</caption>", Caption["<x>"].ToHtml());
}
