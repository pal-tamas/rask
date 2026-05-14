using System.Globalization;

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

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Value is not null) yield return new("value", Value.Value.ToString(CultureInfo.InvariantCulture));
        if (Min is not null) yield return new("min", Min.Value.ToString(CultureInfo.InvariantCulture));
        if (Max is not null) yield return new("max", Max.Value.ToString(CultureInfo.InvariantCulture));
        if (Low is not null) yield return new("low", Low.Value.ToString(CultureInfo.InvariantCulture));
        if (High is not null) yield return new("high", High.Value.ToString(CultureInfo.InvariantCulture));
        if (Optimum is not null) yield return new("optimum", Optimum.Value.ToString(CultureInfo.InvariantCulture));
        if (Form is not null) yield return new("form", Form);
    }
}
