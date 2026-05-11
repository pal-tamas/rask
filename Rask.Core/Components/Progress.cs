using System.Globalization;

namespace Rask.Core.Components;

public sealed class Progress : Component<Progress.Props>
{
    public Progress(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Progress(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "progress";

    public new sealed record Props(
        double? Value = null,
        double? Max = null,
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

            if (Max is not null)
            {
                yield return new KeyValuePair<string, string?>("max", Max.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
