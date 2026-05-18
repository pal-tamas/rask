using System.Text;

namespace Rask.Core.Components;

public sealed class Time : Element
{
    protected override string TagName => "time";

    public string? DateTime { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (DateTime is not null) AppendAttr(sb, "datetime", DateTime);
    }
}
