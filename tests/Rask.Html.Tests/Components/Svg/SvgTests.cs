namespace Rask.Html.Tests.Components;

public partial class SvgTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<svg></svg>", Svg.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<svg id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" width=\"100\" height=\"50\" " +
            "viewBox=\"0 0 100 50\" preserveAspectRatio=\"xMidYMid meet\" x=\"1\" y=\"2\" " +
            "xmlns=\"http://www.w3.org/2000/svg\"></svg>",
            Svg
                .Width("100")
                .Height("50")
                .ViewBox("0 0 100 50")
                .PreserveAspectRatio("xMidYMid meet")
                .X("1")
                .Y("2")
                .Xmlns("http://www.w3.org/2000/svg")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
}
