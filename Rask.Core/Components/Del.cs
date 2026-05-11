namespace Rask.Core.Components;

public sealed class Del : Component<Del.Props>
{
    public Del(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Del(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "del";

    public new sealed record Props(
        string? Cite = null,
        string? DateTime = null,
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

            if (DateTime is not null)
            {
                yield return new KeyValuePair<string, string?>("datetime", DateTime);
            }
        }
    }
}
