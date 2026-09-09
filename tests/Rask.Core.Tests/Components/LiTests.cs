namespace Rask.Core.Tests.Components;

public partial class LiTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<li></li>", Li.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<li id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"42\"></li>",
            Li.Value(42).Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<li>&lt;x&gt;</li>", Li["<x>"].ToHtml());
}
