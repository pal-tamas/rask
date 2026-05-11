using System.Globalization;

namespace Rask.Core.Components;

public sealed class Li : Component<Li.Props>
{
    public Li(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Li(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "li";

    public new sealed record Props(
        int? Value = null,
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

            if (Value is not null)
            {
                yield return new KeyValuePair<string, string?>("value",
                    Value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
