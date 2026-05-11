using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class VideoTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<video></video>", new Video(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Video.Props(
            "/v.mp4", "/p.jpg", 640, 360,
            true, true, true, true,
            "auto", "anonymous", true,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<video id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/v.mp4\" poster=\"/p.jpg\" width=\"640\" height=\"360\" controls autoplay loop muted preload=\"auto\" crossorigin=\"anonymous\" playsinline></video>",
            new Video(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<video>&lt;x&gt;</video>", new Video(null, "<x>").ToHtml());
}
