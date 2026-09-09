using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A closed shape through a list of points; the last point is joined back to the first automatically.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/polygon">MDN</see>
/// </summary>
public sealed partial class Polygon : SvgGeometryElement
{
    protected override string TagName => "polygon";

    /// <summary>The vertices, as space- or comma-separated <c>x,y</c> pairs.</summary>
    public string? Points { get; set; }


    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Points is not null)
        {
            AppendAttr(sb, "points", Points);
        }

    }
}
