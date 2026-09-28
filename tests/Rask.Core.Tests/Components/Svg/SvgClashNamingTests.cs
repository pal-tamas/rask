namespace Rask.Core.Tests.Components;

// The SVG tags HTML also has (a, script, style, title), and path and text (System.IO.Path, Rask's Text), have
// an Svg-prefixed entry that builds MDN's SVG type and renders the real tag. These pin both the rendering and the
// coexistence with the HTML originals.
public partial class SvgClashNamingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Svg_title_renders_title_tag_distinct_from_html_title()
    {
        var svg = SvgTitle["icon"];
        var html = Title["Page"];

        var rendered = (svg.ToHtml(), html.ToHtml());

        Assert.Equal(("<title>icon</title>", "<title>Page</title>"), rendered);
        Assert.IsType<SVGTitleElement>(svg);
        Assert.IsType<HTMLTitleElement>(html);
    }

    [Fact]
    public void Each_prefixed_entry_builds_the_SVG_type_and_renders_the_plain_tag()
    {
        Component[] built = [SvgA["x"], SvgScript, SvgStyle, SvgPath, SvgText];

        var rendered = built.Select(static c => (c.GetType().Name, c.ToHtml())).ToArray();

        Assert.Equal(
            [
                ("SVGAElement", "<a>x</a>"), ("SVGScriptElement", "<script></script>"), ("SVGStyleElement", "<style></style>"),
                ("SVGPathElement", "<path></path>"), ("SVGTextElement", "<text></text>"),
            ],
            rendered);
    }

    [Fact]
    public void An_svg_link_renders_its_target_and_href()
    {
        var link = SvgA.Href("/docs").Target("_blank")["link"];

        var html = link.ToHtml();

        Assert.Equal("<a target=\"_blank\" href=\"/docs\">link</a>", html);
    }
}
