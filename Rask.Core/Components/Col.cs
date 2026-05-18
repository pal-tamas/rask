using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Col : Element
{
    protected override string TagName => "col";
    protected override bool SelfClosing => true;

    public int? Span { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Span is not null) AppendAttr(sb, "span", Span.Value.ToString(CultureInfo.InvariantCulture));
    }
}
