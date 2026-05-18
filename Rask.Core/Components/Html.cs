using System.Text;

namespace Rask.Core.Components;

public sealed class Html : Element
{
    protected override string TagName => "html";

    public string? Lang { get; set; }
    public string? Dir { get; set; }
    public string? Xmlns { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Lang is not null) AppendAttr(sb, "lang", Lang);
        if (Dir is not null) AppendAttr(sb, "dir", Dir);
        if (Xmlns is not null) AppendAttr(sb, "xmlns", Xmlns);
    }
}
