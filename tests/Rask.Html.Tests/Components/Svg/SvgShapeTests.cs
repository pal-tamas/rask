namespace Rask.Html.Tests.Components;

public partial class SvgShapeTests : global::Rask.Core.RaskMarkup
{
    // pathLength renders FIRST on every shape below, because it is declared on
    // SvgGeometryElement - MDN's own grouping for the seven shapes that share it - and a base
    // writes its attributes before the leaf writes its geometry. The order carries no meaning in
    // SVG; what these assertions pin is that the attribute is emitted at all, once, from one place.

    [Fact]
    public void SvgPath_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<path></path>", SvgPath.ToHtml());

    [Fact]
    public void SvgPath_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<path pathLength=\"100\" d=\"M0 0 L10 10\"></path>",
            SvgPath.D("M0 0 L10 10").PathLength("100").ToHtml());

    [Fact]
    public void Rect_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rect></rect>", Rect.ToHtml());

    [Fact]
    public void Rect_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<rect pathLength=\"7\" x=\"1\" y=\"2\" width=\"3\" height=\"4\" rx=\"5\" ry=\"6\"></rect>",
            Rect.X("1").Y("2").Width("3").Height("4").Rx("5").Ry("6").PathLength("7").ToHtml());

    [Fact]
    public void Circle_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<circle pathLength=\"4\" cx=\"1\" cy=\"2\" r=\"3\"></circle>",
            Circle.Cx("1").Cy("2").R("3").PathLength("4").ToHtml());

    [Fact]
    public void Ellipse_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<ellipse pathLength=\"5\" cx=\"1\" cy=\"2\" rx=\"3\" ry=\"4\"></ellipse>",
            Ellipse.Cx("1").Cy("2").Rx("3").Ry("4").PathLength("5").ToHtml());

    [Fact]
    public void Line_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<line pathLength=\"5\" x1=\"1\" y1=\"2\" x2=\"3\" y2=\"4\"></line>",
            Line.X1("1").Y1("2").X2("3").Y2("4").PathLength("5").ToHtml());

    [Fact]
    public void Polyline_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<polyline pathLength=\"2\" points=\"0,0 1,1\"></polyline>",
            Polyline.Points("0,0 1,1").PathLength("2").ToHtml());

    [Fact]
    public void Polygon_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<polygon pathLength=\"3\" points=\"0,0 1,1 2,0\"></polygon>",
            Polygon.Points("0,0 1,1 2,0").PathLength("3").ToHtml());
}
