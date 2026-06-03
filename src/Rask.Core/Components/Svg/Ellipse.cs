using System.Text;

namespace Rask.Core.Components;

public sealed class Ellipse : SvgElement
{
    protected override string TagName => "ellipse";

    public string? Cx { get; set; }
    public string? Cy { get; set; }
    public string? Rx { get; set; }
    public string? Ry { get; set; }
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
