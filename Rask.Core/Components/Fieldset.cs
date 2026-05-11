namespace Rask.Core.Components;

public sealed class Fieldset : Component<Fieldset.Props>
{
    public Fieldset(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Fieldset(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "fieldset";

    public new sealed record Props(
        bool Disabled = false,
        string? Form = null,
        string? Name = null,
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

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }
        }
    }
}
