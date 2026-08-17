using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

public sealed partial class Li : Element
{
    protected override string TagName => "li";

    public int? Value { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
