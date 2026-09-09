namespace Rask.Core.Tests.Components;

public partial class ColTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<col />", Col.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<col id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\" />",
            Col.Span(3).Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
