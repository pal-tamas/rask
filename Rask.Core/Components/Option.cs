namespace Rask.Core.Components;

public sealed class Option : Component<Option.Props>
{
    public Option(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Option(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "option";

    public new sealed record Props(
        string? Value = null,
        bool Selected = false,
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

            if (Value is not null)
            {
                yield return new KeyValuePair<string, string?>("value", Value);
            }

            if (Selected)
            {
                yield return new KeyValuePair<string, string?>("selected", null);
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
