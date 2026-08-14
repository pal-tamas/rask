namespace Rask.Core.Tests.Components;

public partial class SvgFilterTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Filter_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<filter x=\"0\" y=\"0\" width=\"1\" height=\"1\" filterUnits=\"objectBoundingBox\" " +
            "primitiveUnits=\"userSpaceOnUse\"></filter>",
            Filter
                .X("0")
                .Y("0")
                .Width("1")
                .Height("1")
                .FilterUnits("objectBoundingBox")
                .PrimitiveUnits("userSpaceOnUse").ToHtml());

    [Fact]
    public void FeGaussianBlur_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feGaussianBlur in=\"SourceGraphic\" stdDeviation=\"2\" edgeMode=\"duplicate\" result=\"b\"></feGaussianBlur>",
            FeGaussianBlur.In("SourceGraphic").StdDeviation("2").EdgeMode("duplicate").Result("b").ToHtml());

    [Fact]
    public void FeOffset_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feOffset in=\"b\" dx=\"3\" dy=\"4\" result=\"o\"></feOffset>",
            FeOffset.In("b").Dx("3").Dy("4").Result("o").ToHtml());

    [Fact]
    public void FeBlend_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feBlend in=\"a\" in2=\"b\" mode=\"multiply\" result=\"r\"></feBlend>",
            FeBlend.In("a").In2("b").Mode("multiply").Result("r").ToHtml());

    [Fact]
    public void FeColorMatrix_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feColorMatrix in=\"SourceGraphic\" type=\"saturate\" values=\"0.5\" result=\"r\"></feColorMatrix>",
            FeColorMatrix.In("SourceGraphic").Type("saturate").Values("0.5").Result("r").ToHtml());

    [Fact]
    public void FeComposite_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feComposite in=\"a\" in2=\"b\" operator=\"arithmetic\" k1=\"0\" k2=\"1\" k3=\"1\" k4=\"0\" result=\"r\"></feComposite>",
            FeComposite.In("a").In2("b").Operator("arithmetic").K1("0").K2("1").K3("1").K4("0").Result("r").ToHtml());

    [Fact]
    public void FeMerge_WithMergeNodeChildren_NestsCorrectly() =>
        Assert.Equal(
            "<feMerge><feMergeNode in=\"a\"></feMergeNode><feMergeNode in=\"b\"></feMergeNode></feMerge>",
            FeMerge[FeMergeNode.In("a"), FeMergeNode.In("b")].ToHtml());

    [Fact]
    public void FeFlood_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feFlood flood-color=\"#000\" flood-opacity=\"0.5\" result=\"r\"></feFlood>",
            FeFlood.FloodColor("#000").FloodOpacity("0.5").Result("r").ToHtml());

    [Fact]
    public void FeDropShadow_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feDropShadow in=\"SourceGraphic\" dx=\"2\" dy=\"2\" stdDeviation=\"1\" " +
            "flood-color=\"#000\" flood-opacity=\"0.3\" result=\"r\"></feDropShadow>",
            FeDropShadow
                .In("SourceGraphic")
                .Dx("2")
                .Dy("2")
                .StdDeviation("1")
                .FloodColor("#000")
                .FloodOpacity("0.3")
                .Result("r").ToHtml());
}
