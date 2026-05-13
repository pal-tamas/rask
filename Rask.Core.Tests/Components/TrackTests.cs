using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TrackTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<track />", new Track().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<track id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" kind=\"subtitles\" src=\"/sub.vtt\" srclang=\"en\" label=\"English\" default />",
            new Track { Kind = "subtitles", Src = "/sub.vtt", Srclang = "en", Label = "English", Default = true, Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }
}
