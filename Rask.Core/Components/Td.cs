using System.Globalization;

namespace Rask.Core.Components;

public sealed class Td : Component<Td.Props>
{
    public Td(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Td(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "td";

    public new sealed record Props(
        int? Colspan = null,
        int? Rowspan = null,
        string? Headers = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data)
    {
        public override IEnumerable<KeyValuePair<string, string?>> ToAttributes()
        {
            foreach (var kv in base.ToAttributes())
            {
                yield return kv;
            }

            if (Colspan is not null)
            {
                yield return new KeyValuePair<string, string?>("colspan",
                    Colspan.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Rowspan is not null)
            {
                yield return new KeyValuePair<string, string?>("rowspan",
                    Rowspan.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Headers is not null)
            {
                yield return new KeyValuePair<string, string?>("headers", Headers);
            }
        }
    }
}
