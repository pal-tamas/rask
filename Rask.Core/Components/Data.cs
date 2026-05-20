using System.Text;

namespace Rask.Core.Components;

public sealed class Data : Element
{
    protected override string TagName => "data";

    public string? Value { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }
    }
}
