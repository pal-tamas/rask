using System.Text;

namespace Rask.Html.Components;

// MDN models this as a MIXIN (SVGFilterPrimitiveStandardAttributes) rather than an interface, because
// the filter primitives inherit from SVGElement and share these attributes sideways. C# has no mixins,
// but here the shape works out: every filter primitive is a direct child of SVGElement in the DOM too,
// so one abstract base between them expresses the same thing without distorting the hierarchy.
//
// It carries `result` - the attribute all of them share and seven of them each declared. `in` is NOT
// here: it is not a standard attribute, feFlood has none, and feBlend takes two (in and in2), so it
// stays on the concrete tags where the DOM puts it.
//
// feMergeNode is deliberately absent. It is not a filter primitive; it is a child of feMerge that names
// one input, and MDN gives it SVGFEMergeNodeElement with no standard attributes at all.

/// <summary>
///     The filter primitives — the <c>fe*</c> elements a <c>&lt;filter&gt;</c> is built from. Not a tag
///     of its own: it exists so the attribute naming a primitive's output is declared once.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SVGFilterPrimitiveStandardAttributes">MDN</see>
/// </summary>
public abstract partial class SvgFilterPrimitiveElement : SvgElement
{
    /// <summary>
    ///     Names this primitive's output so a later one can take it as its <c>in</c>. Unnamed, the result
    ///     is still available to the next primitive in document order and nowhere else.
    /// </summary>
    public string? Result { get; set; }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
