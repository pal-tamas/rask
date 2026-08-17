using System.Text;

namespace Rask.Html.Components;

public sealed partial class Line : SvgElement
{
    protected override string TagName => "line";

    public string? X1 { get; set; }
    public string? Y1 { get; set; }
    public string? X2 { get; set; }
    public string? Y2 { get; set; }
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
