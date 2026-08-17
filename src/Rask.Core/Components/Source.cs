using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     One media or image candidate for its parent <c>picture</c>, <c>audio</c>, or <c>video</c>. The
///     browser takes the first it can use, so order matters.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/source">MDN</see>
/// </summary>
public sealed class Source : Element
{
    protected override string TagName => "source";
    protected override bool SelfClosing => true;

    /// <summary>
    ///     The resource's URL. For <c>audio</c>/<c>video</c> only — inside a <c>picture</c>, use
    ///     <c>Srcset</c>.
    /// </summary>
    public string? Src { get; set; }

    /// <summary>
    ///     The MIME type, optionally with a codecs parameter, which lets the browser skip a candidate
    ///     without fetching it.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Candidate images with their width or density descriptors. For <c>picture</c>.</summary>
    public string? Srcset { get; set; }

    /// <summary>
    ///     How wide the image will render at given breakpoints, so the browser can pick from <c>Srcset</c>
    ///     before layout.
    /// </summary>
    public string? Sizes { get; set; }

    /// <summary>A media query that must match for this candidate to be considered.</summary>
    public string? Media { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendMediaUrlAttr(sb, "src", Src);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Srcset is not null)
        {
            AppendAttr(sb, "srcset", Srcset);
        }

        if (Sizes is not null)
        {
            AppendAttr(sb, "sizes", Sizes);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }
    }
}
