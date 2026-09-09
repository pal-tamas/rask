using System.Text;

namespace Rask.Html.Components;

// MDN puts `transform` on SVGGraphicsElement, not on SVGElement, and the distinction is real: a
// gradient stop, a filter primitive or a <defs> is not rendered and has nothing to transform. This repo
// had it on SvgElement, so every one of those carried a transform attribute it cannot use - and offered
// it as a chain step and a factory parameter on all of them.
//
// The elements below it are the ones the DOM calls graphics: the seven geometry shapes (through
// SvgGeometryElement), plus g, svg, use, image, foreignObject, switch, symbol, a, and the text
// elements.

/// <summary>
///     The SVG elements that are actually rendered, and can therefore be transformed. Not a tag of its
///     own: it exists so <c>transform</c> is declared once, and only where it applies.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SVGGraphicsElement">MDN</see>
/// </summary>
public abstract partial class SvgGraphicsElement : SvgElement
{
    /// <summary>
    ///     The transform list applied to this element and everything inside it — <c>translate()</c>,
    ///     <c>rotate()</c>, <c>scale()</c>, <c>matrix()</c>.
    /// </summary>
    public string? Transform { get; set; }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Transform is not null)
        {
            AppendAttr(sb, "transform", Transform);
        }
    }
}
