using System.Text;

namespace Rask.Core.Components;

public sealed class Dialog : Element
{
    protected override string TagName => "dialog";

    public bool? Open { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Open is true)
        {
            AppendAttr(sb, "open", null);
        }
    }
}
