using System.Globalization;

namespace Rask.Core.Components;

public sealed class Ol : Component<Ol.Props>
{
    public Ol(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Ol(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "ol";

    public new sealed record Props(
        string? Type = null,
        bool Reversed = false,
        int? Start = null,
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

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Reversed)
            {
                yield return new KeyValuePair<string, string?>("reversed", null);
            }

            if (Start is not null)
            {
                yield return new KeyValuePair<string, string?>("start",
                    Start.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
