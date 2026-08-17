using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A rectangle, optionally with rounded corners.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/rect">MDN</see>
/// </summary>
public sealed class Rect : SvgElement
{
    protected override string TagName => "rect";

    /// <summary>The left edge's x coordinate.</summary>
    public string? X { get; set; }

    /// <summary>The top edge's y coordinate.</summary>
    public string? Y { get; set; }

    /// <summary>The rectangle's width. Zero disables rendering.</summary>
    public string? Width { get; set; }

    /// <summary>The rectangle's height. Zero disables rendering.</summary>
    public string? Height { get; set; }

    /// <summary>
    ///     The corner radius on the x axis. Setting only one of the two radii mirrors it to the other.
    /// </summary>
    public string? Rx { get; set; }

    /// <summary>The corner radius on the y axis.</summary>
    public string? Ry { get; set; }

    /// <summary>
    ///     The length the browser should pretend the outline has, for dash patterns expressed as fractions.
    /// </summary>
    public string? PathLength { get; set; }

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

        if (Rx is not null)
        {
            AppendAttr(sb, "rx", Rx);
        }

        if (Ry is not null)
        {
            AppendAttr(sb, "ry", Ry);
        }

        if (PathLength is not null)
        {
            AppendAttr(sb, "pathLength", PathLength);
        }
    }
}
