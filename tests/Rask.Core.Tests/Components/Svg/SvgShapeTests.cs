namespace Rask.Core.Tests.Components;

public class SvgShapeTests
{
    [Fact]
    public void SvgPath_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<path></path>", SvgPath().ToHtml());

    [Fact]
    public void SvgPath_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<path d=\"M0 0 L10 10\" pathLength=\"100\"></path>",
            SvgPath("M0 0 L10 10", "100").ToHtml());

    [Fact]
    public void Rect_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rect></rect>", Rect().ToHtml());

    [Fact]
    public void Rect_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<rect x=\"1\" y=\"2\" width=\"3\" height=\"4\" rx=\"5\" ry=\"6\" pathLength=\"7\"></rect>",
            Rect("1", "2", "3", "4", "5", "6", "7").ToHtml());

    [Fact]
    public void Circle_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<circle cx=\"1\" cy=\"2\" r=\"3\" pathLength=\"4\"></circle>",
            Circle("1", "2", "3", "4").ToHtml());

    [Fact]
    public void Ellipse_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<ellipse cx=\"1\" cy=\"2\" rx=\"3\" ry=\"4\" pathLength=\"5\"></ellipse>",
            Ellipse("1", "2", "3", "4", "5").ToHtml());

    [Fact]
    public void Line_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<line x1=\"1\" y1=\"2\" x2=\"3\" y2=\"4\" pathLength=\"5\"></line>",
            Line("1", "2", "3", "4", "5").ToHtml());

    [Fact]
    public void Polyline_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<polyline points=\"0,0 1,1\" pathLength=\"2\"></polyline>",
            Polyline("0,0 1,1", "2").ToHtml());

    [Fact]
    public void Polygon_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<polygon points=\"0,0 1,1 2,0\" pathLength=\"3\"></polygon>",
            Polygon("0,0 1,1 2,0", "3").ToHtml());
}
