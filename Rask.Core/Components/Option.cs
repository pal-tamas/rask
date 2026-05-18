using System.Text;

namespace Rask.Core.Components;

public sealed class Option : Element
{
    protected override string TagName => "option";

    public string? Value { get; set; }
    public bool Selected { get; set; }
    public bool Disabled { get; set; }
    public string? Label { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null) AppendAttr(sb, "value", Value);
        if (Selected) AppendAttr(sb, "selected", null);
        if (Disabled) AppendAttr(sb, "disabled", null);
        if (Label is not null) AppendAttr(sb, "label", Label);
    }
}
