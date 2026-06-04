using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Ol : Element
{
    protected override string TagName => "ol";

    public string? Type { get; set; }
    public bool? Reversed { get; set; }
    public int? Start { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Reversed is true)
        {
            AppendAttr(sb, "reversed", null);
        }

        if (Start is not null)
        {
            AppendAttr(sb, "start", Start.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
