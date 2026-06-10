namespace Rask.Core.Tests.Components;

public class SvgGradientTests
{
    [Fact]
    public void LinearGradient_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<linearGradient x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\" gradientUnits=\"userSpaceOnUse\" " +
            "gradientTransform=\"rotate(45)\" spreadMethod=\"pad\" href=\"#base\"></linearGradient>",
            LinearGradient("0", "0", "1", "1", "userSpaceOnUse",
                "rotate(45)", "pad", "#base").ToHtml());

    [Fact]
    public void RadialGradient_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<radialGradient cx=\"0.5\" cy=\"0.5\" r=\"0.5\" fx=\"0.2\" fy=\"0.3\" fr=\"0.1\" " +
            "gradientUnits=\"objectBoundingBox\" gradientTransform=\"scale(2)\" spreadMethod=\"reflect\" " +
            "href=\"#base\"></radialGradient>",
            RadialGradient("0.5", "0.5", "0.5", "0.2", "0.3", "0.1",
                "objectBoundingBox", "scale(2)",
                "reflect", "#base").ToHtml());

    [Fact]
    public void Stop_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<stop offset=\"50%\" stop-color=\"blue\" stop-opacity=\"0.5\"></stop>",
            Stop("50%", "blue", "0.5").ToHtml());

    [Fact]
    public void Gradient_WithStopChildren_NestsCorrectly() =>
        Assert.Equal(
            "<linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"></stop>" +
            "<stop offset=\"1\" stop-color=\"blue\"></stop></linearGradient>",
            LinearGradient(Id: "g")[
                Stop("0", "red"),
                Stop("1", "blue")].ToHtml());

    [Fact]
    public void Pattern_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<pattern x=\"0\" y=\"0\" width=\"10\" height=\"10\" patternUnits=\"userSpaceOnUse\" " +
            "patternContentUnits=\"userSpaceOnUse\" patternTransform=\"rotate(10)\" " +
            "viewBox=\"0 0 10 10\" preserveAspectRatio=\"none\" href=\"#p\"></pattern>",
            Pattern("0", "0", "10", "10", "userSpaceOnUse",
                "userSpaceOnUse", "rotate(10)",
                "0 0 10 10", "none", "#p").ToHtml());
}
