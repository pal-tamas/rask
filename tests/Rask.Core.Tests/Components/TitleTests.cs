namespace Rask.Core.Tests.Components;

public partial class TitleTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<title></title>", Title.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<title id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></title>",
            Title.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<title>&lt;x&gt;</title>", Title["<x>"].ToHtml());
}
