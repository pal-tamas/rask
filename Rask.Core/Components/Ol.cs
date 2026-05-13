using System.Globalization;

namespace Rask.Core.Components;

public sealed class Ol : Component
{
    protected override string TagName => "ol";

    public string? Type { get; set; }
    public bool Reversed { get; set; }
    public int? Start { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Type is not null) yield return new("type", Type);
        if (Reversed) yield return new("reversed", null);
        if (Start is not null) yield return new("start", Start.Value.ToString(CultureInfo.InvariantCulture));
    }
}
