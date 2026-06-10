namespace Rask.Core.Tests.Components;

public class SvgContainerTests
{
    [Fact]
    public void G_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<g></g>", G().ToHtml());

    [Fact]
    public void G_TransformViaBase_EmitsTransform() =>
        Assert.Equal("<g transform=\"translate(10,20)\"></g>", G(Transform: "translate(10,20)").ToHtml());

    [Fact]
    public void Defs_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<defs></defs>", Defs().ToHtml());

    [Fact]
    public void Switch_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<switch></switch>", Switch().ToHtml());

    [Fact]
    public void Desc_WithChild_RendersDescription() =>
        Assert.Equal("<desc>a chart</desc>", Desc()["a chart"].ToHtml());

    [Fact]
    public void Use_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<use href=\"#icon\" x=\"1\" y=\"2\" width=\"3\" height=\"4\"></use>",
            Use("#icon", "1", "2", "3", "4").ToHtml());

    [Fact]
    public void Symbol_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<symbol viewBox=\"0 0 24 24\" preserveAspectRatio=\"xMinYMin\" x=\"1\" y=\"2\" " +
            "width=\"3\" height=\"4\" refX=\"5\" refY=\"6\"></symbol>",
            Symbol("0 0 24 24", "xMinYMin", "1", "2",
                "3", "4", "5", "6").ToHtml());

    [Fact]
    public void Marker_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<marker markerWidth=\"10\" markerHeight=\"10\" refX=\"5\" refY=\"5\" orient=\"auto\" " +
            "markerUnits=\"strokeWidth\" viewBox=\"0 0 10 10\" preserveAspectRatio=\"none\"></marker>",
            Marker("10", "10", "5", "5", "auto",
                "strokeWidth", "0 0 10 10", "none").ToHtml());

    [Fact]
    public void ForeignObject_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<foreignObject x=\"1\" y=\"2\" width=\"3\" height=\"4\"></foreignObject>",
            ForeignObject("1", "2", "3", "4").ToHtml());

    [Fact]
    public void Image_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<image x=\"1\" y=\"2\" width=\"3\" height=\"4\" href=\"/a.png\" " +
            "preserveAspectRatio=\"xMidYMid slice\"></image>",
            Image("1", "2", "3", "4", "/a.png",
                "xMidYMid slice").ToHtml());
}
