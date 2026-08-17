using System.Text;

namespace Rask.Html.Components;

// SVG <text>. Named SvgText to avoid colliding with the Text component (a text node).

/// <summary>
///     Text drawn as part of the graphic — selectable, searchable and readable by assistive technology,
///     unlike text baked into a path. Named <c>SvgText</c> so it does not collide with Rask's <c>Text</c>
///     primitive.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/text">MDN</see>
/// </summary>
public sealed partial class SvgText : SvgElement
{
    protected override string TagName => "text";

    /// <summary>The x coordinate of the text's starting point.</summary>
    public string? X { get; set; }

    /// <summary>The y coordinate of the text's baseline.</summary>
    public string? Y { get; set; }

    /// <summary>A horizontal shift from the current position.</summary>
    public string? Dx { get; set; }

    /// <summary>A vertical shift from the current position.</summary>
    public string? Dy { get; set; }

    /// <summary>Per-glyph rotation in degrees; the last value applies to every remaining glyph.</summary>
    public string? Rotate { get; set; }

    /// <summary>
    ///     Which part of the text sits at <c>X</c>: <c>start</c>, <c>middle</c>, or <c>end</c>.
    /// </summary>
    public string? TextAnchor { get; set; }

    /// <summary>
    ///     Which baseline aligns with <c>Y</c> — <c>middle</c> and <c>central</c> are how you vertically
    ///     centre a label.
    /// </summary>
    public string? DominantBaseline { get; set; }

    /// <summary>The font family, as in CSS.</summary>
    public string? FontFamily { get; set; }

    /// <summary>The font size, as in CSS.</summary>
    public string? FontSize { get; set; }

    /// <summary>The font weight, as in CSS.</summary>
    public string? FontWeight { get; set; }

    /// <summary>
    ///     What <c>TextLength</c> stretches: <c>spacing</c> alone, or <c>spacingAndGlyphs</c>.
    /// </summary>
    public string? LengthAdjust { get; set; }

    /// <summary>The exact width the text must occupy; the browser adjusts spacing to make it fit.</summary>
    public string? TextLength { get; set; }

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

        if (TextAnchor is not null)
        {
            AppendAttr(sb, "text-anchor", TextAnchor);
        }

        if (DominantBaseline is not null)
        {
            AppendAttr(sb, "dominant-baseline", DominantBaseline);
        }

        if (FontFamily is not null)
        {
            AppendAttr(sb, "font-family", FontFamily);
        }

        if (FontSize is not null)
        {
            AppendAttr(sb, "font-size", FontSize);
        }

        if (FontWeight is not null)
        {
            AppendAttr(sb, "font-weight", FontWeight);
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
