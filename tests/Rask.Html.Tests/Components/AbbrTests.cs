namespace Rask.Html.Tests.Components;

public partial class AbbrTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<abbr></abbr>", Abbr.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<abbr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></abbr>",
            Abbr.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<abbr>&lt;x&gt;</abbr>", Abbr["<x>"].ToHtml());
}
