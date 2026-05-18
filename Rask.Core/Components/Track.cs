using System.Text;

namespace Rask.Core.Components;

public sealed class Track : Element
{
    protected override string TagName => "track";
    protected override bool SelfClosing => true;

    public string? Kind { get; set; }
    public string? Src { get; set; }
    public string? Srclang { get; set; }
    public string? Label { get; set; }
    public bool Default { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Kind is not null) AppendAttr(sb, "kind", Kind);
        if (Src is not null) AppendAttr(sb, "src", Src);
        if (Srclang is not null) AppendAttr(sb, "srclang", Srclang);
        if (Label is not null) AppendAttr(sb, "label", Label);
        if (Default) AppendAttr(sb, "default", null);
    }
}
