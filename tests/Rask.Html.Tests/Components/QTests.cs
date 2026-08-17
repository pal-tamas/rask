namespace Rask.Html.Tests.Components;

public partial class QTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<q></q>", Q.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<q id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\"></q>",
            Q.Cite("https://x").Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<q>&lt;x&gt;</q>", Q["<x>"].ToHtml());
}
