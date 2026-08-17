namespace Rask.Html.Tests.Components;

public partial class SupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<sup></sup>", Sup.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<sup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></sup>",
            Sup.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<sup>&lt;x&gt;</sup>", Sup["<x>"].ToHtml());
}
