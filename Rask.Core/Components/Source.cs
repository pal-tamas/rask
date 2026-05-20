using System.Text;

namespace Rask.Core.Components;

public sealed class Source : Element
{
    protected override string TagName => "source";
    protected override bool SelfClosing => true;

    public string? Src { get; set; }
    public string? Type { get; set; }
    public string? Srcset { get; set; }
    public string? Sizes { get; set; }
    public string? Media { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendAttr(sb, "src", Src);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Srcset is not null)
        {
            AppendAttr(sb, "srcset", Srcset);
        }

        if (Sizes is not null)
        {
            AppendAttr(sb, "sizes", Sizes);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }
    }
}
