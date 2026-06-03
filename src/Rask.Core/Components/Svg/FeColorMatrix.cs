using System.Text;

namespace Rask.Core.Components;

public sealed class FeColorMatrix : SvgElement
{
    protected override string TagName => "feColorMatrix";

    public string? In { get; set; }
    public string? Type { get; set; }
    public string? Values { get; set; }
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Values is not null)
        {
            AppendAttr(sb, "values", Values);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
