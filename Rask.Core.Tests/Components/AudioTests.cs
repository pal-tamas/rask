using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AudioTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<audio></audio>", new Audio(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Audio.Props(
            "/a.mp3", true, true, true,
            true, "auto", "anonymous",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<audio id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.mp3\" controls autoplay loop muted preload=\"auto\" crossorigin=\"anonymous\"></audio>",
            new Audio(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<audio>&lt;x&gt;</audio>", new Audio(null, "<x>").ToHtml());
}
