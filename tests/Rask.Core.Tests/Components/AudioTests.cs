namespace Rask.Core.Tests.Components;

public class AudioTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<audio></audio>", Audio().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        // Named arguments: HtmlMediaElement now also contributes the media-event params (OnPlay, …),
        // which sort between the media attrs and Element's Id/Class/Style, so positional id/class/style
        // would no longer line up. The emitted attribute order is unchanged.
        Assert.Equal(
            "<audio id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.mp3\" controls autoplay loop muted preload=\"auto\" crossorigin=\"anonymous\"></audio>",
            Audio(Src: "/a.mp3", Controls: true, Autoplay: true, Loop: true, Muted: true, Preload: "auto",
                CrossOrigin: "anonymous", Id: "i", Class: "c", Style: "s",
                Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<audio>&lt;x&gt;</audio>", Audio()["<x>"].ToHtml());
}
