namespace Rask.Html.Tests.Components;

public partial class BdoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<bdo></bdo>", Bdo.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        // `dir` is a GLOBAL attribute now (#693), so it emits with the plain globals — before the
        // data-* group — rather than as a bdo-specific attribute after it.
        Assert.Equal("<bdo id=\"i\" class=\"c\" style=\"s\" dir=\"rtl\" data-k=\"v\"></bdo>",
            Bdo.Dir("rtl").Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<bdo>&lt;x&gt;</bdo>", Bdo["<x>"].ToHtml());
}
