namespace Rask.Html.Tests.Components;

public partial class SmallTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<small></small>", Small.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<small id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></small>",
            Small.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<small>&lt;x&gt;</small>", Small["<x>"].ToHtml());
}
