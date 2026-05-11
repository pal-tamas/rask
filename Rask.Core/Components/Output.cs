namespace Rask.Core.Components;

public sealed class Output : Component<Output.Props>
{
    public Output(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Output(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "output";

    public new sealed record Props(
        string? For = null,
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

            if (For is not null)
            {
                yield return new KeyValuePair<string, string?>("for", For);
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
