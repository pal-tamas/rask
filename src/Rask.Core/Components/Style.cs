using System.Text;

namespace Rask.Core.Components;

public sealed class Style : Element
{
    protected override string TagName => "style";

    public string? Type { get; set; }
    public string? Media { get; set; }

    // `title` on <style> names an alternative stylesheet, but it is the same global attribute every
    // element has — so it is inherited from Element rather than redeclared here, and renders in the
    // global slot (with id/class/style) instead of among the tag-specific ones.
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

        if (Nonce is not null)
        {
            AppendAttr(sb, "nonce", Nonce);
        }
    }
}
