namespace Rask.Core.Tests.Components;

public class SourceTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<source />", Source().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<source id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.mp4\" type=\"video/mp4\" srcset=\"/a.png 1x\" sizes=\"100vw\" media=\"(min-width:600px)\" />",
            Source("/a.mp4", "video/mp4", "/a.png 1x", "100vw", "(min-width:600px)", "i", "c", "s",
                new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
