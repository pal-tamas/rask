namespace Rask.Core.Tests.Components;

public partial class VideoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<video></video>", Video.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        // Src and the rest of the shared HTMLMediaElement attributes now come from the
        // HtmlMediaElement base, so they emit before Video's own poster/width/height/playsinline.
        // Named arguments keep the call independent of the factory parameter layout.
        Assert.Equal(
            "<video id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/v.mp4\" controls autoplay loop muted preload=\"auto\" crossorigin=\"anonymous\" poster=\"/p.jpg\" width=\"640\" height=\"360\" playsinline></video>",
            Video
                .Src("/v.mp4")
                .Poster("/p.jpg")
                .Width(640)
                .Height(360)
                .Controls(true)
                .Autoplay(true)
                .Loop(true)
                .Muted(true)
                .Preload("auto")
                .CrossOrigin("anonymous")
                .PlaysInline(true)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<video>&lt;x&gt;</video>", Video["<x>"].ToHtml());

    [Fact]
    public void The_playback_restrictions_come_after_the_video_s_own_attributes() =>
        Assert.Equal(
            "<video width=\"640\" playsinline controlslist=\"nodownload nofullscreen\" "
            + "disablepictureinpicture disableremoteplayback loading=\"lazy\"></video>",
            Video
                .Width(640)
                .PlaysInline(true)
                .ControlsList("nodownload nofullscreen")
                .DisablePictureInPicture(true)
                .DisableRemotePlayback(true)
                .Loading("lazy").ToHtml());

    [Fact]
    public void The_playback_restrictions_emit_nothing_when_they_are_off() =>
        // Bare boolean attributes: presence is the value, so false must render nothing at all.
        Assert.Equal("<video></video>",
            Video.DisablePictureInPicture(false).DisableRemotePlayback(false).ToHtml());
}
