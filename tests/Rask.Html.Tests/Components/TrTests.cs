namespace Rask.Html.Tests.Components;

public partial class TrTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tr></tr>", Tr.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<tr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tr>",
            Tr.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tr>&lt;x&gt;</tr>", Tr["<x>"].ToHtml());
}
