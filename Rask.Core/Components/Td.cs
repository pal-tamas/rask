using System.Globalization;

namespace Rask.Core.Components;

public sealed class Td : Element
{
    protected override string TagName => "td";

    public int? Colspan { get; set; }
    public int? Rowspan { get; set; }
    public string? Headers { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Colspan is not null) yield return new("colspan", Colspan.Value.ToString(CultureInfo.InvariantCulture));
        if (Rowspan is not null) yield return new("rowspan", Rowspan.Value.ToString(CultureInfo.InvariantCulture));
        if (Headers is not null) yield return new("headers", Headers);
    }
}
