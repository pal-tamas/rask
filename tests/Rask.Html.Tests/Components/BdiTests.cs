namespace Rask.Html.Tests.Components;

public partial class BdiTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<bdi></bdi>", Bdi.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<bdi id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></bdi>",
            Bdi.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<bdi>&lt;x&gt;</bdi>", Bdi["<x>"].ToHtml());
}
