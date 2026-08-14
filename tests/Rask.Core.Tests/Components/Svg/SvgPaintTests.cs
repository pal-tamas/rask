namespace Rask.Core.Tests.Components;

public partial class SvgPaintTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void ClipPath_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<clipPath clipPathUnits=\"userSpaceOnUse\"></clipPath>",
            ClipPath.ClipPathUnits("userSpaceOnUse").ToHtml());

    [Fact]
    public void ClipPath_InheritedClipPathPresentationProp_StillAvailable() =>
        // The element type and the inherited `clip-path` presentation property share a name but
        // are distinct symbols; setting the inherited one emits the clip-path attribute.
        Assert.Equal(
            "<clipPath clip-path=\"url(#c)\"></clipPath>",
            ClipPath.ClipPath("url(#c)").ToHtml());

    [Fact]
    public void Mask_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<mask maskUnits=\"userSpaceOnUse\" maskContentUnits=\"userSpaceOnUse\" " +
            "x=\"0\" y=\"0\" width=\"10\" height=\"10\"></mask>",
            Mask
                .MaskUnits("userSpaceOnUse")
                .MaskContentUnits("userSpaceOnUse")
                .X("0")
                .Y("0")
                .Width("10")
                .Height("10").ToHtml());
}
