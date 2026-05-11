using System.Globalization;

namespace Rask.Core.Components;

public sealed class Th : Component<Th.Props>
{
    public Th(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Th(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "th";

    public new sealed record Props(
        int? Colspan = null,
        int? Rowspan = null,
        string? Headers = null,
        string? Scope = null,
        string? Abbr = null,
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

            if (Scope is not null)
            {
                yield return new KeyValuePair<string, string?>("scope", Scope);
            }

            if (Abbr is not null)
            {
                yield return new KeyValuePair<string, string?>("abbr", Abbr);
            }
        }
    }
}
