using System.Globalization;

namespace Rask.Core.Components;

public sealed class Colgroup : Component
{
    protected override string TagName => "colgroup";

    public int? Span { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Span is not null) yield return new("span", Span.Value.ToString(CultureInfo.InvariantCulture));
    }
}
