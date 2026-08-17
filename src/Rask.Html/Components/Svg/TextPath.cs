using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     Lays text along the shape of a referenced <c>path</c> instead of a straight baseline.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/textPath">MDN</see>
/// </summary>
public sealed partial class TextPath : SvgElement
{
    protected override string TagName => "textPath";

    /// <summary>The <c>#id</c> of the path to lay the text along.</summary>
    public string? Href { get; set; }

    /// <summary>How far along the path the text begins, as a length or a percentage.</summary>
    public string? StartOffset { get; set; }

    /// <summary>How glyphs are placed on the curve: <c>align</c> (the default) or <c>stretch</c>.</summary>
    public string? Method { get; set; }

    /// <summary>How spacing is handled around the curve: <c>auto</c> or <c>exact</c>.</summary>
    public string? Spacing { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (StartOffset is not null)
        {
            AppendAttr(sb, "startOffset", StartOffset);
        }

        if (Method is not null)
        {
            AppendAttr(sb, "method", Method);
        }

        if (Spacing is not null)
        {
            AppendAttr(sb, "spacing", Spacing);
        }
    }
}
