namespace Rask.Core.Components;

public sealed class Label : Component<Label.Props>
{
    public Label(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Label(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "label";

    public new sealed record Props(
        string? For = null,
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

            if (For is not null)
            {
                yield return new KeyValuePair<string, string?>("for", For);
            }

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }
        }
    }
}
