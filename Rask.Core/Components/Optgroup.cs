namespace Rask.Core.Components;

public sealed class Optgroup : Component<Optgroup.Props>
{
    public Optgroup(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Optgroup(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "optgroup";

    public new sealed record Props(
        bool Disabled = false,
        string? Label = null,
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

            if (Disabled)
            {
                yield return new KeyValuePair<string, string?>("disabled", null);
            }

            if (Label is not null)
            {
                yield return new KeyValuePair<string, string?>("label", Label);
            }
        }
    }
}
