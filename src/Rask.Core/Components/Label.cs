using System.Text;

namespace Rask.Core.Components;

public sealed class Label : Element
{
    protected override string TagName => "label";

    public string? For { get; set; }
    public new string? Form { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (For is not null)
        {
            AppendAttr(sb, "for", For);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }
    }
}
