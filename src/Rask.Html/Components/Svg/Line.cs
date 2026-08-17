using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A straight line between two points. It has no interior, so only <c>Stroke</c> paints it — a line
///     with no stroke is invisible.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/line">MDN</see>
/// </summary>
public sealed partial class Line : SvgElement
{
    protected override string TagName => "line";

    /// <summary>The start point's x coordinate.</summary>
    public string? X1 { get; set; }

    /// <summary>The start point's y coordinate.</summary>
    public string? Y1 { get; set; }

    /// <summary>The end point's x coordinate.</summary>
    public string? X2 { get; set; }

    /// <summary>The end point's y coordinate.</summary>
    public string? Y2 { get; set; }

    /// <summary>
    ///     The length the browser should pretend the line has, for dash patterns expressed as fractions.
    /// </summary>
    public string? PathLength { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (X1 is not null)
        {
            AppendAttr(sb, "x1", X1);
        }

        if (Y1 is not null)
        {
            AppendAttr(sb, "y1", Y1);
        }

        if (X2 is not null)
        {
            AppendAttr(sb, "x2", X2);
        }

        if (Y2 is not null)
        {
            AppendAttr(sb, "y2", Y2);
        }

        if (PathLength is not null)
        {
            AppendAttr(sb, "pathLength", PathLength);
        }
    }
}
