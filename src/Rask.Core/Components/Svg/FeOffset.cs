using System.Text;

namespace Rask.Core.Components;

public sealed class FeOffset : SvgElement
{
    protected override string TagName => "feOffset";

    public string? In { get; set; }
    public string? Dx { get; set; }
    public string? Dy { get; set; }
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (Dx is not null)
        {
            AppendAttr(sb, "dx", Dx);
        }

        if (Dy is not null)
        {
            AppendAttr(sb, "dy", Dy);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
