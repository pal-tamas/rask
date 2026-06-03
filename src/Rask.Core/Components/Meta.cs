using System.Text;

namespace Rask.Core.Components;

public sealed class Meta : Element
{
    protected override string TagName => "meta";
    protected override bool SelfClosing => true;

    public string? Charset { get; set; }
    public string? Name { get; set; }
    public string? Content { get; set; }
    public string? HttpEquiv { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Charset is not null)
        {
            AppendAttr(sb, "charset", Charset);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Content is not null)
        {
            AppendAttr(sb, "content", Content);
        }

        if (HttpEquiv is not null)
        {
            AppendAttr(sb, "http-equiv", HttpEquiv);
        }
    }
}
