using System.Text;

namespace Rask.Core.Components;

public sealed class Del : Element
{
    protected override string TagName => "del";

    public string? Cite { get; set; }
    public string? DateTime { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cite is not null)
        {
            AppendAttr(sb, "cite", Cite);
        }

        if (DateTime is not null)
        {
            AppendAttr(sb, "datetime", DateTime);
        }
    }
}
