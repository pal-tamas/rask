using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Td : Element
{
    protected override string TagName => "td";

    public int? Colspan { get; set; }
    public int? Rowspan { get; set; }
    public string? Headers { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Colspan is not null)
        {
            AppendAttr(sb, "colspan", Colspan.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Rowspan is not null)
        {
            AppendAttr(sb, "rowspan", Rowspan.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Headers is not null)
        {
            AppendAttr(sb, "headers", Headers);
        }
    }
}
