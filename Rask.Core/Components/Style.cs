using System.Text;

namespace Rask.Core.Components;

public sealed class Style : Element
{
    protected override string TagName => "style";

    public string? Type { get; set; }
    public string? Media { get; set; }
    public string? Title { get; set; }
    public string? Nonce { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }

        if (Title is not null)
        {
            AppendAttr(sb, "title", Title);
        }

        if (Nonce is not null)
        {
            AppendAttr(sb, "nonce", Nonce);
        }
    }
}
