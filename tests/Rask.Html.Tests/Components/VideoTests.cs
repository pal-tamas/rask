namespace Rask.Html.Tests.Components;

public partial class VideoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<video></video>", Video.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
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
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<video>&lt;x&gt;</video>", Video["<x>"].ToHtml());

    [Fact]
    public void Render_PlaybackRestrictions_EmitThemAfterTheVideoAttrs() =>
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
    public void Render_PlaybackRestrictions_EmitNothingWhenFalse() =>
        // Bare boolean attributes: presence is the value, so false must render nothing at all.
        Assert.Equal("<video></video>",
            Video.DisablePictureInPicture(false).DisableRemotePlayback(false).ToHtml());
}
