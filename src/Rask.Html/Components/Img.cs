using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     An embedded image. <c>Alt</c> is not optional in practice: it is what a screen-reader user gets
///     instead of the picture, and RASK is strict about it — set it to the empty string only when the image
///     is purely decorative.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/img">MDN</see>
/// </summary>
public sealed partial class Img : Element
{
    protected override string TagName => "img";
    protected override bool SelfClosing => true;

    /// <summary>The image's URL. Required.</summary>
    public string? Src { get; set; }

    /// <summary>
    ///     The text that replaces the image for anyone who cannot see it. Describe the image's function,
    ///     not its appearance; use an empty string for a decorative image so it is skipped rather than
    ///     announced by filename.
    /// </summary>
    public string? Alt { get; set; }

    /// <summary>
    ///     The intrinsic width in pixels. Set it together with <c>Height</c> so the browser can reserve the
    ///     space and avoid a layout shift as the image loads.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    ///     The intrinsic height in pixels. Set it together with <c>Width</c> to reserve layout space up
    ///     front.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    ///     <c>lazy</c> defers the fetch until the image nears the viewport; <c>eager</c> (the default)
    ///     fetches immediately. Never lazy-load an image that is visible on first paint.
    /// </summary>
    public string? Loading { get; set; }

    /// <summary>
    ///     Candidate images with width (<c>800w</c>) or density (<c>2x</c>) descriptors, letting the
    ///     browser pick one to fit the screen.
    /// </summary>
    public string? Srcset { get; set; }

    /// <summary>
    ///     How wide the image will actually render, per media condition, so the browser can choose from
    ///     <c>Srcset</c> before layout happens.
    /// </summary>
    public string? Sizes { get; set; }

    /// <summary>
    ///     The CORS mode for the fetch — <c>anonymous</c> or <c>use-credentials</c>. Required before the
    ///     image can be read back from a canvas.
    /// </summary>
    public string? CrossOrigin { get; set; }

    /// <summary>How much of the referrer to send when fetching the image.</summary>
    public string? ReferrerPolicy { get; set; }

    /// <summary>A hint for when to decode: <c>sync</c>, <c>async</c>, or <c>auto</c>.</summary>
    public string? Decoding { get; set; }

    /// <summary>The <c>#name</c> of a <c>map</c> element that turns this image into an image map.</summary>
    public string? UseMap { get; set; }

    /// <summary>
    ///     Marks the image a server-side image map, which sends the click coordinates to the server. Only
    ///     meaningful inside a link.
    /// </summary>
    public bool? Ismap { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendMediaUrlAttr(sb, "src", Src);
        }

        if (Alt is not null)
        {
            AppendAttr(sb, "alt", Alt);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Loading is not null)
        {
            AppendAttr(sb, "loading", Loading);
        }

        if (Srcset is not null)
        {
            AppendAttr(sb, "srcset", Srcset);
        }

        if (Sizes is not null)
        {
            AppendAttr(sb, "sizes", Sizes);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Decoding is not null)
        {
            AppendAttr(sb, "decoding", Decoding);
        }

        if (UseMap is not null)
        {
            AppendAttr(sb, "usemap", UseMap);
        }

        if (Ismap is true)
        {
            AppendAttr(sb, "ismap", null);
        }
    }
}
