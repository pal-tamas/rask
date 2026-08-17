using System.Text;

namespace Rask.Html.Components;

public sealed partial class Dialog : Element
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
