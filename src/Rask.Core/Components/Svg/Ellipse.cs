using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     An ellipse, positioned by its centre and given two radii.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/ellipse">MDN</see>
/// </summary>
public sealed class Ellipse : SvgElement
{
    protected override string TagName => "ellipse";

    /// <summary>The centre's x coordinate.</summary>
    public string? Cx { get; set; }

    /// <summary>The centre's y coordinate.</summary>
    public string? Cy { get; set; }

    /// <summary>The horizontal radius.</summary>
    public string? Rx { get; set; }

    /// <summary>The vertical radius.</summary>
    public string? Ry { get; set; }

    /// <summary>
    ///     The length the browser should pretend the outline has, for dash patterns expressed as fractions.
    /// </summary>
    public string? PathLength { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cx is not null)
        {
            AppendAttr(sb, "cx", Cx);
        }

        if (Cy is not null)
        {
            AppendAttr(sb, "cy", Cy);
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
