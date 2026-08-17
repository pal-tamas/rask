using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A rectangle inside the graphic whose children come from another namespace — HTML, typically. The
///     escape hatch for putting wrapped, laid-out HTML text inside an SVG.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/foreignObject">MDN</see>
/// </summary>
public sealed partial class ForeignObject : SvgElement
{
    protected override string TagName => "foreignObject";

    /// <summary>The left edge's x coordinate.</summary>
    public string? X { get; set; }

    /// <summary>The top edge's y coordinate.</summary>
    public string? Y { get; set; }

    /// <summary>The rectangle's width. Zero disables rendering.</summary>
    public string? Width { get; set; }

    /// <summary>The rectangle's height. Zero disables rendering.</summary>
    public string? Height { get; set; }

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
    }
}
