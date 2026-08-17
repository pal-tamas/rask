namespace Rask.Html.Tests.Components;

public partial class LabelTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<label></label>", Label.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<label id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" for=\"name\" form=\"f\"></label>",
            Label
                .For("name")
                .Form("f")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<label>&lt;x&gt;</label>", Label["<x>"].ToHtml());
}
