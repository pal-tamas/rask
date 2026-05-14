using System.Globalization;

namespace Rask.Core.Components;

public sealed class Col : Element
{
    protected override string TagName => "col";
    protected override bool SelfClosing => true;

    public int? Span { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Span is not null) yield return new("span", Span.Value.ToString(CultureInfo.InvariantCulture));
    }
}
