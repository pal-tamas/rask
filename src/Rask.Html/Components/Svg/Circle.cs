using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A circle, positioned by its centre.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/circle">MDN</see>
/// </summary>
public sealed partial class Circle : SvgGeometryElement
{
    protected override string TagName => "circle";

    /// <summary>The centre's x coordinate.</summary>
    public string? Cx { get; set; }

    /// <summary>The centre's y coordinate.</summary>
    public string? Cy { get; set; }

    /// <summary>The radius. A zero or negative radius disables rendering.</summary>
    public string? R { get; set; }


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

        if (R is not null)
        {
            AppendAttr(sb, "r", R);
        }

    }
}
