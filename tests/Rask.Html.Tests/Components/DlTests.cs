namespace Rask.Html.Tests.Components;

public partial class DlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dl></dl>", Dl.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<dl id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dl>",
            Dl.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dl>&lt;x&gt;</dl>", Dl["<x>"].ToHtml());
}
