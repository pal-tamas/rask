namespace Rask.Html.Tests.Components;

public partial class ITests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<i></i>", I.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<i id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></i>",
            I.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<i>&lt;x&gt;</i>", I["<x>"].ToHtml());
}
