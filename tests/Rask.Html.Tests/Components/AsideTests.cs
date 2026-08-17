namespace Rask.Html.Tests.Components;

public partial class AsideTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<aside></aside>", Aside.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<aside id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></aside>",
            Aside.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<aside>&lt;x&gt;</aside>", Aside["<x>"].ToHtml());
}
