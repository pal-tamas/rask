using System.Text;

namespace Rask.Core.Components;

public sealed class Q : Element
{
    protected override string TagName => "q";

    public string? Cite { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cite is not null) AppendAttr(sb, "cite", Cite);
    }
}
