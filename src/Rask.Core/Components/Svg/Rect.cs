using System.Text;

namespace Rask.Core.Components;

public sealed class Rect : SvgElement
{
    protected override string TagName => "rect";

    public string? X { get; set; }
    public string? Y { get; set; }
    public string? Width { get; set; }
    public string? Height { get; set; }
    public string? Rx { get; set; }
    public string? Ry { get; set; }
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
