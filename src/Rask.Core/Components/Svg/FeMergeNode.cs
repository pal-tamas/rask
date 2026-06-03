using System.Text;

namespace Rask.Core.Components;

public sealed class FeMergeNode : SvgElement
{
    protected override string TagName => "feMergeNode";

    public string? In { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }
    }
}
