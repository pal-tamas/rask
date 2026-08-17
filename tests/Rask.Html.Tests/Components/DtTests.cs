namespace Rask.Html.Tests.Components;

public partial class DtTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dt></dt>", Dt.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<dt id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dt>",
            Dt.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dt>&lt;x&gt;</dt>", Dt["<x>"].ToHtml());
}
