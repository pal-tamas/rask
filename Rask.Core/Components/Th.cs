using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Th : Element
{
    protected override string TagName => "th";

    public int? Colspan { get; set; }
    public int? Rowspan { get; set; }
    public string? Headers { get; set; }
    public string? Scope { get; set; }
    public string? Abbr { get; set; }

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

        if (Scope is not null)
        {
            AppendAttr(sb, "scope", Scope);
        }

        if (Abbr is not null)
        {
            AppendAttr(sb, "abbr", Abbr);
        }
    }
}
