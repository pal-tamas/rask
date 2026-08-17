using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A run inside a <c>text</c> element that can be positioned or styled on its own — a second line, a
///     highlighted word, a superscript.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/tspan">MDN</see>
/// </summary>
public sealed partial class Tspan : SvgElement
{
    protected override string TagName => "tspan";

    /// <summary>An absolute x position for this run.</summary>
    public string? X { get; set; }

    /// <summary>An absolute y position for this run. Setting it is how you start a new line.</summary>
    public string? Y { get; set; }

    /// <summary>A horizontal shift from the previous run's end.</summary>
    public string? Dx { get; set; }

    /// <summary>
    ///     A vertical shift from the previous run's end — the relative way to start a new line.
    /// </summary>
    public string? Dy { get; set; }

    /// <summary>Per-glyph rotation in degrees.</summary>
    public string? Rotate { get; set; }

    /// <summary>Which part of the run sits at its x position.</summary>
    public string? TextAnchor { get; set; }

    /// <summary>What <c>TextLength</c> stretches: <c>spacing</c> or <c>spacingAndGlyphs</c>.</summary>
    public string? LengthAdjust { get; set; }

    /// <summary>The exact width this run must occupy.</summary>
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
