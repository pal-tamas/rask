namespace Rask.Html.Tests.Components;

public partial class H6Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h6></h6>", H6.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<h6 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h6>",
            H6.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h6>&lt;x&gt;</h6>", H6["<x>"].ToHtml());
}
