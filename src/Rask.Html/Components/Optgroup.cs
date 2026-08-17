using System.Text;

namespace Rask.Html.Components;

public sealed partial class Optgroup : Element
{
    protected override string TagName => "optgroup";

    public bool? Disabled { get; set; }
    public string? Label { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Label is not null)
        {
            AppendAttr(sb, "label", Label);
        }
    }
}
