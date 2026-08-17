namespace Rask.Html.Tests.Components;

public partial class SvgShapeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void SvgPath_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<path></path>", SvgPath.ToHtml());

    [Fact]
    public void SvgPath_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<path d=\"M0 0 L10 10\" pathLength=\"100\"></path>",
            SvgPath.D("M0 0 L10 10").PathLength("100").ToHtml());

    [Fact]
    public void Rect_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rect></rect>", Rect.ToHtml());

    [Fact]
    public void Rect_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<rect x=\"1\" y=\"2\" width=\"3\" height=\"4\" rx=\"5\" ry=\"6\" pathLength=\"7\"></rect>",
            Rect.X("1").Y("2").Width("3").Height("4").Rx("5").Ry("6").PathLength("7").ToHtml());

    [Fact]
    public void Circle_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<circle cx=\"1\" cy=\"2\" r=\"3\" pathLength=\"4\"></circle>",
            Circle.Cx("1").Cy("2").R("3").PathLength("4").ToHtml());

    [Fact]
    public void Ellipse_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<ellipse cx=\"1\" cy=\"2\" rx=\"3\" ry=\"4\" pathLength=\"5\"></ellipse>",
            Ellipse.Cx("1").Cy("2").Rx("3").Ry("4").PathLength("5").ToHtml());

    [Fact]
    public void Line_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<line x1=\"1\" y1=\"2\" x2=\"3\" y2=\"4\" pathLength=\"5\"></line>",
            Line.X1("1").Y1("2").X2("3").Y2("4").PathLength("5").ToHtml());

    [Fact]
    public void Polyline_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<polyline points=\"0,0 1,1\" pathLength=\"2\"></polyline>",
            Polyline.Points("0,0 1,1").PathLength("2").ToHtml());

    [Fact]
    public void Polygon_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<polygon points=\"0,0 1,1 2,0\" pathLength=\"3\"></polygon>",
            Polygon.Points("0,0 1,1 2,0").PathLength("3").ToHtml());
}
