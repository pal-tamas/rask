using System.Text;

namespace Rask.Html.Components;

public sealed partial class Track : Element
{
    protected override string TagName => "track";
    protected override bool SelfClosing => true;

    public string? Kind { get; set; }
    public string? Src { get; set; }
    public string? Srclang { get; set; }
    public string? Label { get; set; }
    public bool? Default { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Kind is not null)
        {
            AppendAttr(sb, "kind", Kind);
        }

        if (Src is not null)
        {
            AppendMediaUrlAttr(sb, "src", Src);
        }

        if (Srclang is not null)
        {
            AppendAttr(sb, "srclang", Srclang);
        }

        if (Label is not null)
        {
            AppendAttr(sb, "label", Label);
        }

        if (Default is true)
        {
            AppendAttr(sb, "default", null);
        }
    }
}
