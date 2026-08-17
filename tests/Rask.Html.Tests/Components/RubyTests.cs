namespace Rask.Html.Tests.Components;

public partial class RubyTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ruby></ruby>", Ruby.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<ruby id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></ruby>",
            Ruby.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ruby>&lt;x&gt;</ruby>", Ruby["<x>"].ToHtml());
}
