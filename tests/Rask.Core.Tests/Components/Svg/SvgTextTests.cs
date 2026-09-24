namespace Rask.Core.Tests.Components;

public partial class SvgTextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_on_a_svg_text_render_only_the_open_and_close_tags() =>
        Assert.Equal("<text></text>", SvgText.ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_svg_text_emits_the_expected_attributes() =>
        Assert.Equal(
            "<text x=\"1\" y=\"2\" dx=\"3\" dy=\"4\" rotate=\"5\" text-anchor=\"middle\" " +
            "dominant-baseline=\"central\" font-family=\"sans-serif\" font-size=\"12\" " +
            "font-weight=\"bold\" lengthAdjust=\"spacing\" textLength=\"80\">hi</text>",
            SvgText
                .X("1")
                .Y("2")
                .Dx("3")
                .Dy("4")
                .Rotate("5")
                .TextAnchor("middle")
                .DominantBaseline("central")
                .FontFamily("sans-serif")
                .FontSize("12")
                .FontWeight("bold")
                .LengthAdjust("spacing")
                .TextLength("80")["hi"].ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_tspan_emits_the_expected_attributes() =>
        Assert.Equal(
            "<tspan x=\"1\" y=\"2\" dx=\"3\" dy=\"4\" rotate=\"5\" text-anchor=\"end\" " +
            "lengthAdjust=\"spacingAndGlyphs\" textLength=\"40\">x</tspan>",
            Tspan
                .X("1")
                .Y("2")
                .Dx("3")
                .Dy("4")
                .Rotate("5")
                .TextAnchor("end")
                .LengthAdjust("spacingAndGlyphs")
                .TextLength("40")["x"].ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_text_path_emits_the_expected_attributes() =>
        Assert.Equal(
            "<textPath href=\"#p\" startOffset=\"10%\" method=\"align\" spacing=\"auto\">label</textPath>",
            TextPath.Href("#p").StartOffset("10%").Method("align").Spacing("auto")["label"].ToHtml());
}
