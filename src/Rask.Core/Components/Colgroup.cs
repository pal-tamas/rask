using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Colgroup : Element
{
    protected override string TagName => "colgroup";

    public int? Span { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Span is not null)
        {
            AppendAttr(sb, "span", Span.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
