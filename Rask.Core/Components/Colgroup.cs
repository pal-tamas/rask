using System.Globalization;

namespace Rask.Core.Components;

public sealed class Colgroup : Component<Colgroup.Props>
{
    public Colgroup(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Colgroup(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "colgroup";

    public new sealed record Props(
        int? Span = null,
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

            if (Span is not null)
            {
                yield return new KeyValuePair<string, string?>("span",
                    Span.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
