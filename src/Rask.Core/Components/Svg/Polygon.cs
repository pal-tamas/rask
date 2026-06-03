using System.Text;

namespace Rask.Core.Components;

public sealed class Polygon : SvgElement
{
    protected override string TagName => "polygon";

    public string? Points { get; set; }
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
