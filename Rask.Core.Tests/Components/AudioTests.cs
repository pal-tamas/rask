using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AudioTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<audio></audio>", new Audio().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<audio id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.mp3\" controls autoplay loop muted preload=\"auto\" crossorigin=\"anonymous\"></audio>",
            new Audio { Src = "/a.mp3", Controls = true, Autoplay = true, Loop = true, Muted = true, Preload = "auto", CrossOrigin = "anonymous", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<audio>&lt;x&gt;</audio>", new Audio { Children = ["<x>"] }.ToHtml());
}
