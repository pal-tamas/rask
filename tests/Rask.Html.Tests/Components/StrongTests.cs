namespace Rask.Html.Tests.Components;

public partial class StrongTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<strong></strong>", Strong.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<strong id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></strong>",
            Strong.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<strong>&lt;x&gt;</strong>", Strong["<x>"].ToHtml());
}
