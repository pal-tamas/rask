using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

public sealed partial class Progress : Element
{
    protected override string TagName => "progress";

    public double? Value { get; set; }
    public double? Max { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Max is not null)
        {
            AppendAttr(sb, "max", Max.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
