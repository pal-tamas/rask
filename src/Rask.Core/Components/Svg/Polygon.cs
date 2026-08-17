using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A closed shape through a list of points; the last point is joined back to the first automatically.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/polygon">MDN</see>
/// </summary>
public sealed class Polygon : SvgElement
{
    protected override string TagName => "polygon";

    /// <summary>The vertices, as space- or comma-separated <c>x,y</c> pairs.</summary>
    public string? Points { get; set; }

    /// <summary>
    ///     The length the browser should pretend the outline has, for dash patterns expressed as fractions.
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
