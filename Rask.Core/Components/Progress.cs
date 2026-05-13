using System.Globalization;

namespace Rask.Core.Components;

public sealed class Progress : Component
{
    protected override string TagName => "progress";

    public double? Value { get; set; }
    public double? Max { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Value is not null) yield return new("value", Value.Value.ToString(CultureInfo.InvariantCulture));
        if (Max is not null) yield return new("max", Max.Value.ToString(CultureInfo.InvariantCulture));
    }
}
