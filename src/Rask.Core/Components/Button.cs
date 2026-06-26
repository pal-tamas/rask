using System.Text;

namespace Rask.Core.Components;

public sealed class Button : Element
{
    protected override string TagName => "button";

    public string? Type { get; set; }
    public bool? Disabled { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }

    // OnClick / OnClickAsync are inherited from Element (the GlobalEventHandlers surface).

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }
    }
}
