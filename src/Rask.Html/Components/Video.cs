using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     An embedded video player. Give it <c>Controls</c> unless you are building your own, and a
///     <c>track</c> child for captions — captions are the difference between a usable video and an
///     inaccessible one.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/video">MDN</see>
/// </summary>
public sealed partial class Video : HtmlMediaElement
{
    protected override string TagName => "video";

    /// <summary>
    ///     A still image shown until the first frame is available. Without it the player shows the first
    ///     frame, which is often black.
    /// </summary>
    public string? Poster { get; set; }

    /// <summary>The display width in CSS pixels.</summary>
    public int? Width { get; set; }

    /// <summary>The display height in CSS pixels.</summary>
    public int? Height { get; set; }

    /// <summary>
    ///     Plays inline rather than taking over the screen. Needed on iOS, where video otherwise opens
    ///     fullscreen.
    /// </summary>
    public bool? PlaysInline { get; set; }

    /// <summary>
    ///     Hides individual native controls: any of <c>nodownload</c>, <c>nofullscreen</c>,
    ///     <c>noremoteplayback</c>, space-separated. A hint, not a security boundary — the media is still
    ///     fetchable by anyone who looks.
    /// </summary>
    public string? ControlsList { get; set; }

    /// <summary>Suppresses the picture-in-picture affordance.</summary>
    public bool? DisablePictureInPicture { get; set; }

    /// <summary>Suppresses casting to a remote device (Chromecast, AirPlay).</summary>
    public bool? DisableRemotePlayback { get; set; }

    /// <summary>
    ///     <c>lazy</c> defers loading until the video is near the viewport — worth setting on anything
    ///     below the fold, where the poster and metadata are otherwise fetched immediately.
    /// </summary>
    public string? Loading { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        // Emits the universal attrs then the shared HtmlMediaElement block (src, controls, …,
        // crossorigin); the video-specific attrs below follow that block.
        base.WriteAttributes(sb);

        if (Poster is not null)
        {
            AppendMediaUrlAttr(sb, "poster", Poster);
        }

        if (Width is { } width)
        {
            AppendAttr(sb, "width", width);
        }

        if (Height is { } height)
        {
            AppendAttr(sb, "height", height);
        }

        if (PlaysInline is true)
        {
            AppendAttr(sb, "playsinline", null);
        }

        if (ControlsList is not null)
        {
            AppendAttr(sb, "controlslist", ControlsList);
        }

        if (DisablePictureInPicture is true)
        {
            AppendAttr(sb, "disablepictureinpicture", null);
        }

        if (DisableRemotePlayback is true)
        {
            AppendAttr(sb, "disableremoteplayback", null);
        }

        if (Loading is not null)
        {
            AppendAttr(sb, "loading", Loading);
        }
    }
}
