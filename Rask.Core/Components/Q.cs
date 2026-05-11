namespace Rask.Core.Components;

public sealed class Q : Component<Q.Props>
{
    public Q(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Q(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "q";

    public new sealed record Props(
        string? Cite = null,
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

            if (Cite is not null)
            {
                yield return new KeyValuePair<string, string?>("cite", Cite);
            }
        }
    }
}
