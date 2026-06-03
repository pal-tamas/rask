namespace Rask.Core.Tests.Components;

public class TrackTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<track />", Track().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<track id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" kind=\"subtitles\" src=\"/sub.vtt\" srclang=\"en\" label=\"English\" default />",
            Track("subtitles", "/sub.vtt", "en", "English", true, "i", "c", "s",
                new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
