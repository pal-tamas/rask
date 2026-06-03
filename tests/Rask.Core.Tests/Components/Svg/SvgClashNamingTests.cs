namespace Rask.Core.Tests.Components;

// The five SVG tags whose names collide with HTML factories get an Svg prefix but keep the real
// SVG tag name in their output. These tests pin both the rendering and the coexistence with the
// HTML originals.
public class SvgClashNamingTests
{
    [Fact]
    public void SvgTitle_RendersTitleTag_DistinctFromHtmlTitle()
    {
        Assert.Equal("<title>icon</title>", SvgTitle()["icon"].ToHtml());
        Assert.Equal("<title>Page</title>", Title()["Page"].ToHtml());
    }

    [Fact]
    public void SvgA_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<a href=\"/docs\" target=\"_blank\">link</a>",
            SvgA(Href: "/docs", Target: "_blank")["link"].ToHtml());

    [Fact]
    public void SvgScript_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<script href=\"/a.js\" type=\"text/javascript\"></script>",
            SvgScript(Href: "/a.js", Type: "text/javascript").ToHtml());

    [Fact]
    public void SvgStyle_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<style type=\"text/css\" media=\"screen\">.x{fill:red}</style>",
            SvgStyle(Type: "text/css", Media: "screen")[Raw(".x{fill:red}")].ToHtml());
}
