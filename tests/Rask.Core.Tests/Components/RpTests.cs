namespace Rask.Core.Tests.Components;

public partial class RpTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rp></rp>", Rp.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<rp id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></rp>",
            Rp.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<rp>&lt;x&gt;</rp>", Rp["<x>"].ToHtml());
}
