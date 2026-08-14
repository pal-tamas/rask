namespace Rask.Core.Tests.Components;

public partial class SvgGradientTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void LinearGradient_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<linearGradient x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\" gradientUnits=\"userSpaceOnUse\" " +
            "gradientTransform=\"rotate(45)\" spreadMethod=\"pad\" href=\"#base\"></linearGradient>",
            LinearGradient
                .X1("0")
                .Y1("0")
                .X2("1")
                .Y2("1")
                .GradientUnits("userSpaceOnUse")
                .GradientTransform("rotate(45)")
                .SpreadMethod("pad")
                .Href("#base").ToHtml());

    [Fact]
    public void RadialGradient_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<radialGradient cx=\"0.5\" cy=\"0.5\" r=\"0.5\" fx=\"0.2\" fy=\"0.3\" fr=\"0.1\" " +
            "gradientUnits=\"objectBoundingBox\" gradientTransform=\"scale(2)\" spreadMethod=\"reflect\" " +
            "href=\"#base\"></radialGradient>",
            RadialGradient
                .Cx("0.5")
                .Cy("0.5")
                .R("0.5")
                .Fx("0.2")
                .Fy("0.3")
                .Fr("0.1")
                .GradientUnits("objectBoundingBox")
                .GradientTransform("scale(2)")
                .SpreadMethod("reflect")
                .Href("#base").ToHtml());

    [Fact]
    public void Stop_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<stop offset=\"50%\" stop-color=\"blue\" stop-opacity=\"0.5\"></stop>",
            Stop.Offset("50%").StopColor("blue").StopOpacity("0.5").ToHtml());

    [Fact]
    public void Gradient_WithStopChildren_NestsCorrectly() =>
        Assert.Equal(
            "<linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"></stop>" +
            "<stop offset=\"1\" stop-color=\"blue\"></stop></linearGradient>",
            LinearGradient.Id("g")[
                Stop.Offset("0").StopColor("red"),
                Stop.Offset("1").StopColor("blue")].ToHtml());

    [Fact]
    public void Pattern_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<pattern x=\"0\" y=\"0\" width=\"10\" height=\"10\" patternUnits=\"userSpaceOnUse\" " +
            "patternContentUnits=\"userSpaceOnUse\" patternTransform=\"rotate(10)\" " +
            "viewBox=\"0 0 10 10\" preserveAspectRatio=\"none\" href=\"#p\"></pattern>",
            Pattern
                .X("0")
                .Y("0")
                .Width("10")
                .Height("10")
                .PatternUnits("userSpaceOnUse")
                .PatternContentUnits("userSpaceOnUse")
                .PatternTransform("rotate(10)")
                .ViewBox("0 0 10 10")
                .PreserveAspectRatio("none")
                .Href("#p").ToHtml());
}
