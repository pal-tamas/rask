using System.Text;

namespace Rask.Core.Components;

public sealed class FeGaussianBlur : SvgElement
{
    protected override string TagName => "feGaussianBlur";

    public string? In { get; set; }
    public string? StdDeviation { get; set; }
    public string? EdgeMode { get; set; }
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (StdDeviation is not null)
        {
            AppendAttr(sb, "stdDeviation", StdDeviation);
        }

        if (EdgeMode is not null)
        {
            AppendAttr(sb, "edgeMode", EdgeMode);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
