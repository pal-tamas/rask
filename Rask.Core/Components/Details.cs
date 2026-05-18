using System.Text;

namespace Rask.Core.Components;

public sealed class Details : Element
{
    protected override string TagName => "details";

    public bool Open { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Open) AppendAttr(sb, "open", null);
    }
}
