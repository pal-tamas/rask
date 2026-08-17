using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A graphic drawn at the vertices of a path, line, polyline or polygon — arrowheads, most often.
///     Attach it with the <c>marker-start</c>, <c>marker-mid</c> and <c>marker-end</c> CSS properties.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/marker">MDN</see>
/// </summary>
public sealed class Marker : SvgElement
{
    protected override string TagName => "marker";

    /// <summary>The marker viewport's width.</summary>
    public string? MarkerWidth { get; set; }

    /// <summary>The marker viewport's height.</summary>
    public string? MarkerHeight { get; set; }

    /// <summary>
    ///     The x coordinate inside the marker that is placed on the vertex — how you line an arrowhead up
    ///     with the line's end.
    /// </summary>
    public string? RefX { get; set; }

    /// <summary>The y coordinate inside the marker that is placed on the vertex.</summary>
    public string? RefY { get; set; }

    /// <summary>
    ///     How the marker is rotated: an angle, <c>auto</c> to follow the path's direction, or
    ///     <c>auto-start-reverse</c> to flip the one at the start.
    /// </summary>
    public string? Orient { get; set; }

    /// <summary>
    ///     Whether the marker scales with the stroke width (<c>strokeWidth</c>, the default) or not
    ///     (<c>userSpaceOnUse</c>).
    /// </summary>
    public string? MarkerUnits { get; set; }

    /// <summary>A user-coordinate rectangle mapped onto the marker viewport.</summary>
    public string? ViewBox { get; set; }

    /// <summary>How to fit the <c>ViewBox</c> into the marker viewport.</summary>
    public string? PreserveAspectRatio { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (MarkerWidth is not null)
        {
            AppendAttr(sb, "markerWidth", MarkerWidth);
        }

        if (MarkerHeight is not null)
        {
            AppendAttr(sb, "markerHeight", MarkerHeight);
        }

        if (RefX is not null)
        {
            AppendAttr(sb, "refX", RefX);
        }

        if (RefY is not null)
        {
            AppendAttr(sb, "refY", RefY);
        }

        if (Orient is not null)
        {
            AppendAttr(sb, "orient", Orient);
        }

        if (MarkerUnits is not null)
        {
            AppendAttr(sb, "markerUnits", MarkerUnits);
        }

        if (ViewBox is not null)
        {
            AppendAttr(sb, "viewBox", ViewBox);
        }

        if (PreserveAspectRatio is not null)
        {
            AppendAttr(sb, "preserveAspectRatio", PreserveAspectRatio);
        }
    }
}
