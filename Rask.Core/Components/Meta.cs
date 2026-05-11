namespace Rask.Core.Components;

public sealed class Meta : Component<Meta.Props>
{
    public Meta(Props? props = null) : base(props, null) { }

    protected override string TagName => "meta";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Charset = null,
        string? Name = null,
        string? Content = null,
        string? HttpEquiv = null,
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

            if (Charset is not null)
            {
                yield return new KeyValuePair<string, string?>("charset", Charset);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }

            if (Content is not null)
            {
                yield return new KeyValuePair<string, string?>("content", Content);
            }

            if (HttpEquiv is not null)
            {
                yield return new KeyValuePair<string, string?>("http-equiv", HttpEquiv);
            }
        }
    }
}
