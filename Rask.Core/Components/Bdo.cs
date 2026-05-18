using System.Text;

namespace Rask.Core.Components;

public sealed class Bdo : Element
{
    protected override string TagName => "bdo";

    public string? Dir { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Dir is not null) AppendAttr(sb, "dir", Dir);
    }
}
