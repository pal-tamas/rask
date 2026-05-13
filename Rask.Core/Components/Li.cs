using System.Globalization;

namespace Rask.Core.Components;

public sealed class Li : Component
{
    protected override string TagName => "li";

    public int? Value { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Value is not null) yield return new("value", Value.Value.ToString(CultureInfo.InvariantCulture));
    }
}
