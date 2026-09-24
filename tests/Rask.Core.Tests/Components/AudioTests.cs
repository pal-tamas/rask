namespace Rask.Core.Tests.Components;

public partial class AudioTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<audio></audio>", Audio.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        // Named arguments: HtmlMediaElement now also contributes the media-event params (OnPlay, …),
        // which sort between the media attrs and Element's Id/Class/Style, so positional id/class/style
        // would no longer line up. The emitted attribute order is unchanged.
        Assert.Equal(
            "<audio id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.mp3\" controls autoplay loop muted preload=\"auto\" crossorigin=\"anonymous\"></audio>",
            Audio
                .Src("/a.mp3")
                .Controls(true)
                .Autoplay(true)
                .Loop(true)
                .Muted(true)
                .Preload("auto")
                .CrossOrigin("anonymous")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<audio>&lt;x&gt;</audio>", Audio["<x>"].ToHtml());
}
