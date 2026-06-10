namespace Rask.Core.Tests.Components;

public class SvgTextTests
{
    [Fact]
    public void SvgText_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<text></text>", SvgText().ToHtml());

    [Fact]
    public void SvgText_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<text x=\"1\" y=\"2\" dx=\"3\" dy=\"4\" rotate=\"5\" text-anchor=\"middle\" " +
            "dominant-baseline=\"central\" font-family=\"sans-serif\" font-size=\"12\" " +
            "font-weight=\"bold\" lengthAdjust=\"spacing\" textLength=\"80\">hi</text>",
            SvgText("1", "2", "3", "4", "5", "middle",
                "central", "sans-serif", "12",
                "bold", "spacing", "80")["hi"].ToHtml());

    [Fact]
    public void Tspan_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<tspan x=\"1\" y=\"2\" dx=\"3\" dy=\"4\" rotate=\"5\" text-anchor=\"end\" " +
            "lengthAdjust=\"spacingAndGlyphs\" textLength=\"40\">x</tspan>",
            Tspan("1", "2", "3", "4", "5", "end",
                "spacingAndGlyphs", "40")["x"].ToHtml());

    [Fact]
    public void TextPath_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<textPath href=\"#p\" startOffset=\"10%\" method=\"align\" spacing=\"auto\">label</textPath>",
            TextPath("#p", "10%", "align", "auto")["label"].ToHtml());
}
