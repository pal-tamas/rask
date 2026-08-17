using System.Text;

namespace Rask.Html.Components;

public sealed partial class FeComposite : SvgElement
{
    protected override string TagName => "feComposite";

    public string? In { get; set; }
    public string? In2 { get; set; }
    public string? Operator { get; set; }
    public string? K1 { get; set; }
    public string? K2 { get; set; }
    public string? K3 { get; set; }
    public string? K4 { get; set; }
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

        if (Operator is not null)
        {
            AppendAttr(sb, "operator", Operator);
        }

        if (K1 is not null)
        {
            AppendAttr(sb, "k1", K1);
        }

        if (K2 is not null)
        {
            AppendAttr(sb, "k2", K2);
        }

        if (K3 is not null)
        {
            AppendAttr(sb, "k3", K3);
        }

        if (K4 is not null)
        {
            AppendAttr(sb, "k4", K4);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
