namespace Rask.Core.Tests.Components;

public class SvgPaintTests
{
    [Fact]
    public void ClipPath_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<clipPath clipPathUnits=\"userSpaceOnUse\"></clipPath>",
            ClipPath("userSpaceOnUse").ToHtml());

    [Fact]
    public void ClipPath_InheritedClipPathPresentationProp_StillAvailable() =>
        // The element type and the inherited `clip-path` presentation property share a name but
        // are distinct symbols; setting the inherited one emits the clip-path attribute.
        Assert.Equal(
            "<clipPath clip-path=\"url(#c)\"></clipPath>",
            ClipPath(ClipPath: "url(#c)").ToHtml());

    [Fact]
    public void Mask_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<mask maskUnits=\"userSpaceOnUse\" maskContentUnits=\"userSpaceOnUse\" " +
            "x=\"0\" y=\"0\" width=\"10\" height=\"10\"></mask>",
            Mask("userSpaceOnUse", "userSpaceOnUse",
                "0", "0", "10", "10").ToHtml());
}
