namespace Rask.Html.Tests.Components;

public partial class SvgFilterTests : global::Rask.Core.RaskMarkup
{
    // `result` renders FIRST on every primitive below: it is declared on
    // SvgFilterPrimitiveElement - MDN's SVGFilterPrimitiveStandardAttributes, the attributes every
    // fe* element shares - and a base writes its attributes before the leaf writes its own.

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
            "<feGaussianBlur result=\"b\" in=\"SourceGraphic\" stdDeviation=\"2\" edgeMode=\"duplicate\"></feGaussianBlur>",
            FeGaussianBlur.In("SourceGraphic").StdDeviation("2").EdgeMode("duplicate").Result("b").ToHtml());

    [Fact]
    public void FeOffset_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feOffset result=\"o\" in=\"b\" dx=\"3\" dy=\"4\"></feOffset>",
            FeOffset.In("b").Dx("3").Dy("4").Result("o").ToHtml());

    [Fact]
    public void FeBlend_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feBlend result=\"r\" in=\"a\" in2=\"b\" mode=\"multiply\"></feBlend>",
            FeBlend.In("a").In2("b").Mode("multiply").Result("r").ToHtml());

    [Fact]
    public void FeColorMatrix_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feColorMatrix result=\"r\" in=\"SourceGraphic\" type=\"saturate\" values=\"0.5\"></feColorMatrix>",
            FeColorMatrix.In("SourceGraphic").Type("saturate").Values("0.5").Result("r").ToHtml());

    [Fact]
    public void FeComposite_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feComposite result=\"r\" in=\"a\" in2=\"b\" operator=\"arithmetic\" k1=\"0\" k2=\"1\" k3=\"1\" k4=\"0\"></feComposite>",
            FeComposite.In("a").In2("b").Operator("arithmetic").K1("0").K2("1").K3("1").K4("0").Result("r").ToHtml());

    [Fact]
    public void FeMerge_WithMergeNodeChildren_NestsCorrectly() =>
        Assert.Equal(
            "<feMerge><feMergeNode in=\"a\"></feMergeNode><feMergeNode in=\"b\"></feMergeNode></feMerge>",
            FeMerge[FeMergeNode.In("a"), FeMergeNode.In("b")].ToHtml());

    [Fact]
    public void FeFlood_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feFlood result=\"r\" flood-color=\"#000\" flood-opacity=\"0.5\"></feFlood>",
            FeFlood.FloodColor("#000").FloodOpacity("0.5").Result("r").ToHtml());

    [Fact]
    public void FeDropShadow_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<feDropShadow result=\"r\" in=\"SourceGraphic\" dx=\"2\" dy=\"2\" stdDeviation=\"1\" " +
            "flood-color=\"#000\" flood-opacity=\"0.3\"></feDropShadow>",
            FeDropShadow
                .In("SourceGraphic")
                .Dx("2")
                .Dy("2")
                .StdDeviation("1")
                .FloodColor("#000")
                .FloodOpacity("0.3")
                .Result("r").ToHtml());
}
