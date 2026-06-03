using System.Text;

namespace Rask.Core.Components;

public sealed class FeBlend : SvgElement
{
    protected override string TagName => "feBlend";

    public string? In { get; set; }
    public string? In2 { get; set; }
    public string? Mode { get; set; }
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (In2 is not null)
        {
            AppendAttr(sb, "in2", In2);
        }

        if (Mode is not null)
        {
            AppendAttr(sb, "mode", Mode);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
