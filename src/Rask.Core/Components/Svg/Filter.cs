using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A container for filter primitives — blur, offset, colour matrix — applied to an element via the CSS
///     <c>filter</c> property as <c>url(#id)</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/filter">MDN</see>
/// </summary>
public sealed class Filter : SvgElement
{
    protected override string TagName => "filter";

    /// <summary>
    ///     The left edge of the filter region. Defaults to -10%, giving the effect room to spill past the
    ///     element.
    /// </summary>
    public string? X { get; set; }

    /// <summary>The top edge of the filter region. Defaults to -10%.</summary>
    public string? Y { get; set; }

    /// <summary>
    ///     The filter region's width. Defaults to 120% — enlarge it when a blur or shadow is being clipped.
    /// </summary>
    public string? Width { get; set; }

    /// <summary>The filter region's height. Defaults to 120%.</summary>
    public string? Height { get; set; }

    /// <summary>
    ///     The coordinate system for the filter region: <c>objectBoundingBox</c> (the default) or
    ///     <c>userSpaceOnUse</c>.
    /// </summary>
    public string? FilterUnits { get; set; }

    /// <summary>The coordinate system for the primitives' own lengths.</summary>
    public string? PrimitiveUnits { get; set; }

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

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width);
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height);
        }

        if (FilterUnits is not null)
        {
            AppendAttr(sb, "filterUnits", FilterUnits);
        }

        if (PrimitiveUnits is not null)
        {
            AppendAttr(sb, "primitiveUnits", PrimitiveUnits);
        }
    }
}
