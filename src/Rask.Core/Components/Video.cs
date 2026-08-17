using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     An embedded video player. Give it <c>Controls</c> unless you are building your own, and a
///     <c>track</c> child for captions — captions are the difference between a usable video and an
///     inaccessible one.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/video">MDN</see>
/// </summary>
public sealed class Video : HtmlMediaElement
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
    }
}
