namespace Rask.Html.Tests.Components;

public partial class DfnTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dfn></dfn>", Dfn.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<dfn id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dfn>",
            Dfn.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dfn>&lt;x&gt;</dfn>", Dfn["<x>"].ToHtml());
}
