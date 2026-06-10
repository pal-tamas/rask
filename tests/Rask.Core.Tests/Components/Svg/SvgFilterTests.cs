namespace Rask.Core.Tests.Components;

public class SvgFilterTests
{
    [Fact]
    public void Filter_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<filter x=\"0\" y=\"0\" width=\"1\" height=\"1\" filterUnits=\"objectBoundingBox\" " +
            "primitiveUnits=\"userSpaceOnUse\"></filter>",
            Filter("0", "0", "1", "1", "objectBoundingBox",
                "userSpaceOnUse").ToHtml());

    [Fact]
    public void FeGaussianBlur_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feGaussianBlur in=\"SourceGraphic\" stdDeviation=\"2\" edgeMode=\"duplicate\" result=\"b\"></feGaussianBlur>",
            FeGaussianBlur("SourceGraphic", "2", "duplicate", "b").ToHtml());

    [Fact]
    public void FeOffset_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feOffset in=\"b\" dx=\"3\" dy=\"4\" result=\"o\"></feOffset>",
            FeOffset("b", "3", "4", "o").ToHtml());

    [Fact]
    public void FeBlend_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feBlend in=\"a\" in2=\"b\" mode=\"multiply\" result=\"r\"></feBlend>",
            FeBlend("a", "b", "multiply", "r").ToHtml());

    [Fact]
    public void FeColorMatrix_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feColorMatrix in=\"SourceGraphic\" type=\"saturate\" values=\"0.5\" result=\"r\"></feColorMatrix>",
            FeColorMatrix("SourceGraphic", "saturate", "0.5", "r").ToHtml());

    [Fact]
    public void FeComposite_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feComposite in=\"a\" in2=\"b\" operator=\"arithmetic\" k1=\"0\" k2=\"1\" k3=\"1\" k4=\"0\" result=\"r\"></feComposite>",
            FeComposite("a", "b", "arithmetic", "0", "1", "1", "0", "r").ToHtml());

    [Fact]
    public void FeMerge_WithMergeNodeChildren_NestsCorrectly() =>
        Assert.Equal(
            "<feMerge><feMergeNode in=\"a\"></feMergeNode><feMergeNode in=\"b\"></feMergeNode></feMerge>",
            FeMerge()[FeMergeNode("a"), FeMergeNode("b")].ToHtml());

    [Fact]
    public void FeFlood_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feFlood flood-color=\"#000\" flood-opacity=\"0.5\" result=\"r\"></feFlood>",
            FeFlood("#000", "0.5", "r").ToHtml());

    [Fact]
    public void FeDropShadow_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feDropShadow in=\"SourceGraphic\" dx=\"2\" dy=\"2\" stdDeviation=\"1\" " +
            "flood-color=\"#000\" flood-opacity=\"0.3\" result=\"r\"></feDropShadow>",
            FeDropShadow("SourceGraphic", "2", "2", "1",
                "#000", "0.3", "r").ToHtml());
}
