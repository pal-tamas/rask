using System.Globalization;

namespace Rask.Core.Components;

public sealed class Meter : Component<Meter.Props>
{
    public Meter(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Meter(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "meter";

    public new sealed record Props(
        double? Value = null,
        double? Min = null,
        double? Max = null,
        double? Low = null,
        double? High = null,
        double? Optimum = null,
        string? Form = null,
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

            if (Min is not null)
            {
                yield return new KeyValuePair<string, string?>("min", Min.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Max is not null)
            {
                yield return new KeyValuePair<string, string?>("max", Max.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Low is not null)
            {
                yield return new KeyValuePair<string, string?>("low", Low.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (High is not null)
            {
                yield return new KeyValuePair<string, string?>("high",
                    High.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Optimum is not null)
            {
                yield return new KeyValuePair<string, string?>("optimum",
                    Optimum.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }
        }
    }
}
