using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Meter : Element
{
    protected override string TagName => "meter";

    public double? Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Low { get; set; }
    public double? High { get; set; }
    public double? Optimum { get; set; }
    public string? Form { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null) AppendAttr(sb, "value", Value.Value.ToString(CultureInfo.InvariantCulture));
        if (Min is not null) AppendAttr(sb, "min", Min.Value.ToString(CultureInfo.InvariantCulture));
        if (Max is not null) AppendAttr(sb, "max", Max.Value.ToString(CultureInfo.InvariantCulture));
        if (Low is not null) AppendAttr(sb, "low", Low.Value.ToString(CultureInfo.InvariantCulture));
        if (High is not null) AppendAttr(sb, "high", High.Value.ToString(CultureInfo.InvariantCulture));
        if (Optimum is not null) AppendAttr(sb, "optimum", Optimum.Value.ToString(CultureInfo.InvariantCulture));
        if (Form is not null) AppendAttr(sb, "form", Form);
    }
}
