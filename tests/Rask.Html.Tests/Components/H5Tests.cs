namespace Rask.Html.Tests.Components;

public partial class H5Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h5></h5>", H5.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<h5 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h5>",
            H5.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h5>&lt;x&gt;</h5>", H5["<x>"].ToHtml());
}
