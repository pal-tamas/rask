namespace Rask.Html.Tests.Components;

public partial class H2Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h2></h2>", H2.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<h2 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h2>",
            H2.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h2>&lt;x&gt;</h2>", H2["<x>"].ToHtml());
}
