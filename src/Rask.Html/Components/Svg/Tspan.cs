using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A run inside a <c>text</c> element that can be positioned or styled on its own — a second line, a
///     highlighted word, a superscript.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/tspan">MDN</see>
/// </summary>
public sealed partial class Tspan : SvgTextPositioningElement
{
    protected override string TagName => "tspan";

    /// <summary>An absolute x position for this run.</summary>

    /// <summary>An absolute y position for this run. Setting it is how you start a new line.</summary>

    /// <summary>A horizontal shift from the previous run's end.</summary>


    /// <summary>Per-glyph rotation in degrees.</summary>

    /// <summary>Which part of the run sits at its x position.</summary>

    /// <summary>What <c>TextLength</c> stretches: <c>spacing</c> or <c>spacingAndGlyphs</c>.</summary>

    /// <summary>The exact width this run must occupy.</summary>

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);







    }
}
