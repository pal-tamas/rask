namespace Rask.Html.Tests.Components;

public partial class UTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<u></u>", U.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<u id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></u>",
            U.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<u>&lt;x&gt;</u>", U["<x>"].ToHtml());
}
