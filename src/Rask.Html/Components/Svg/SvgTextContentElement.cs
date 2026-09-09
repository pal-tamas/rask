using System.Text;

namespace Rask.Html.Components;

// MDN's SVGTextContentElement: the elements that hold text — text, tspan and textPath. It carries the
// two attributes that describe how that text is fitted to a length, which `text` and `tspan` each
// declared. `textPath` gains them by joining, which is correct: it is a text content element in the DOM
// and has both.

/// <summary>
///     The SVG elements that contain text — <c>text</c>, <c>tspan</c> and <c>textPath</c>. Not a tag of
///     its own: it exists so the attributes describing how text is fitted are declared once.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SVGTextContentElement">MDN</see>
/// </summary>
public abstract partial class SvgTextContentElement : SvgGraphicsElement
{
    /// <summary>
    ///     Where the text sits relative to its anchor point: <c>start</c>, <c>middle</c> or <c>end</c>.
    /// </summary>
    public string? TextAnchor { get; set; }

    /// <summary>
    ///     Whether <see cref="TextLength" /> is met by stretching the glyphs themselves
    ///     (<c>spacingAndGlyphs</c>) or only the gaps between them (<c>spacing</c>, the default).
    /// </summary>
    public string? LengthAdjust { get; set; }

    /// <summary>
    ///     The width the text should be made to occupy, whatever it would otherwise measure.
    /// </summary>
    public string? TextLength { get; set; }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (TextAnchor is not null)
        {
            AppendAttr(sb, "text-anchor", TextAnchor);
        }

        if (LengthAdjust is not null)
        {
            AppendAttr(sb, "lengthAdjust", LengthAdjust);
        }

        if (TextLength is not null)
        {
            AppendAttr(sb, "textLength", TextLength);
        }
    }
}
