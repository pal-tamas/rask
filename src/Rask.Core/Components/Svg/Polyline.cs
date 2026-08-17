using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A connected series of straight lines through a list of points, left open. Set <c>Fill: "none"</c> —
///     the default fill paints the shape as if it were closed.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/polyline">MDN</see>
/// </summary>
public sealed class Polyline : SvgElement
{
    protected override string TagName => "polyline";

    /// <summary>The vertices, as space- or comma-separated <c>x,y</c> pairs.</summary>
    public string? Points { get; set; }

    /// <summary>
    ///     The length the browser should pretend the line has, for dash patterns expressed as fractions.
    /// </summary>
    public string? PathLength { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Points is not null)
        {
            AppendAttr(sb, "points", Points);
        }

        if (PathLength is not null)
        {
            AppendAttr(sb, "pathLength", PathLength);
        }
    }
}
