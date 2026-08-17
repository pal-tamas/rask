using System.Text;

namespace Rask.Html.Components;

public sealed partial class Fieldset : Element
{
    protected override string TagName => "fieldset";

    public bool? Disabled { get; set; }
    public string? Form { get; set; }
    public string? Name { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }
    }
}
