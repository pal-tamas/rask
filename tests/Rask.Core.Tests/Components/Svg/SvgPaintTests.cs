namespace Rask.Core.Tests.Components;

public partial class SvgPaintTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Setting_every_prop_on_a_clip_path_emits_the_expected_attributes() =>
        Assert.Equal(
            "<clipPath clipPathUnits=\"userSpaceOnUse\"></clipPath>",
            ClipPath.ClipPathUnits("userSpaceOnUse").ToHtml());

    [Fact]
    public void Clip_path_inherited_clip_path_presentation_prop_still_available() =>
        // The element type and the inherited `clip-path` presentation property share a name but
        // are distinct symbols; setting the inherited one emits the clip-path attribute.
        Assert.Equal(
            "<clipPath clip-path=\"url(#c)\"></clipPath>",
            ClipPath.ClipPath("url(#c)").ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_mask_emits_the_expected_attributes() =>
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
