using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A timed text track for its parent <c>audio</c> or <c>video</c>: subtitles, captions, chapters. The
///     file must be WebVTT, and cross-origin tracks need CORS.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/track">MDN</see>
/// </summary>
public sealed partial class Track : Element
{
    protected override string TagName => "track";
    protected override bool SelfClosing => true;

    /// <summary>
    ///     What the track is: <c>subtitles</c>, <c>captions</c>, <c>descriptions</c>, <c>chapters</c>, or
    ///     <c>metadata</c>. Captions include non-speech sound; subtitles are translation only.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>The URL of the WebVTT file. Required.</summary>
    public string? Src { get; set; }

    /// <summary>
    ///     The track's language as a BCP 47 tag. Required when <c>Kind</c> is <c>subtitles</c>.
    /// </summary>
    public string? Srclang { get; set; }

    /// <summary>The title shown in the browser's track-selection menu.</summary>
    public new string? Label { get; set; }

    /// <summary>
    ///     Marks this the track to enable when the user has expressed no preference. At most one per media
    ///     element.
    /// </summary>
    public bool? Default { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Kind is not null)
        {
            AppendAttr(sb, "kind", Kind);
        }

        if (Src is not null)
        {
            AppendMediaUrlAttr(sb, "src", Src);
        }

        if (Srclang is not null)
        {
            AppendAttr(sb, "srclang", Srclang);
        }

        if (Label is not null)
        {
            AppendAttr(sb, "label", Label);
        }

        if (Default is true)
        {
            AppendAttr(sb, "default", null);
        }
    }
}
