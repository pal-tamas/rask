using System.Text;

namespace Rask.Html.Components;

// MDN's SVGTextPositioningElement, between SVGTextContentElement and the two elements that can place
// their text explicitly: text and tspan. Both declared all five of these.
//
// textPath is deliberately NOT here, and the DOM agrees: its text follows a path, so absolute and
// relative positioning would have nothing to mean. It stays on SvgTextContentElement with its own
// startOffset / method / spacing.

/// <summary>
///     The text elements that position their own glyphs — <c>text</c> and <c>tspan</c>. Not a tag of its
///     own: it exists so the five positioning attributes they share are declared once.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SVGTextPositioningElement">MDN</see>
/// </summary>
public abstract partial class SvgTextPositioningElement : SvgTextContentElement
{
    /// <summary>The absolute x coordinate, or a list of them, one per glyph.</summary>
    public string? X { get; set; }

    /// <summary>The absolute y coordinate, or a list of them, one per glyph.</summary>
    public string? Y { get; set; }

    /// <summary>A shift along x from where the glyph would otherwise sit.</summary>
    public string? Dx { get; set; }

    /// <summary>A shift along y from where the glyph would otherwise sit.</summary>
    public string? Dy { get; set; }

    /// <summary>A rotation in degrees, or a list of them, one per glyph.</summary>
    public string? Rotate { get; set; }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (X is not null)
        {
            AppendAttr(sb, "x", X);
        }

        if (Y is not null)
        {
            AppendAttr(sb, "y", Y);
        }

        if (Dx is not null)
        {
            AppendAttr(sb, "dx", Dx);
        }

        if (Dy is not null)
        {
            AppendAttr(sb, "dy", Dy);
        }

        if (Rotate is not null)
        {
            AppendAttr(sb, "rotate", Rotate);
        }
    }
}
