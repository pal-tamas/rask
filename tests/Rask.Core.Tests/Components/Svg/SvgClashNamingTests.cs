namespace Rask.Core.Tests.Components;

// The five SVG tags whose names collide with HTML factories get an Svg prefix but keep the real
// SVG tag name in their output. These tests pin both the rendering and the coexistence with the
// HTML originals.
public partial class SvgClashNamingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Svg_title_renders_title_tag_distinct_from_html_title()
    {
        Assert.Equal("<title>icon</title>", SvgTitle["icon"].ToHtml());
        Assert.Equal("<title>Page</title>", Title["Page"].ToHtml());
    }

    [Fact]
    public void Setting_every_prop_on_a_svg_a_emits_the_expected_attributes() =>
        Assert.Equal(
            "<a href=\"/docs\" target=\"_blank\">link</a>",
            SvgA.Href("/docs").Target("_blank")["link"].ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_svg_script_emits_the_expected_attributes() =>
        Assert.Equal(
            "<script href=\"/a.js\" type=\"text/javascript\"></script>",
            SvgScript.Href("/a.js").Type("text/javascript").ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_svg_style_emits_the_expected_attributes() =>
        Assert.Equal(
            "<style type=\"text/css\" media=\"screen\">.x{fill:red}</style>",
            SvgStyle.Type("text/css").Media("screen")[Raw.Value(".x{fill:red}")].ToHtml());
}
