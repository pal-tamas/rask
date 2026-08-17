using System.Text;

namespace Rask.Html.Components;

public sealed partial class Circle : SvgElement
{
    protected override string TagName => "circle";

    public string? Cx { get; set; }
    public string? Cy { get; set; }
    public string? R { get; set; }
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

        if (R is not null)
        {
            AppendAttr(sb, "r", R);
        }

        if (PathLength is not null)
        {
            AppendAttr(sb, "pathLength", PathLength);
        }
    }
}
