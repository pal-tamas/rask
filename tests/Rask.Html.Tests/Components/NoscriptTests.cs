namespace Rask.Html.Tests.Components;

public partial class NoscriptTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<noscript></noscript>", Noscript.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<noscript id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></noscript>",
            Noscript.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<noscript>&lt;x&gt;</noscript>", Noscript["<x>"].ToHtml());
}
