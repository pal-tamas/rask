namespace Rask.Core.Components;

public sealed class Style : Component<Style.Props>
{
    public Style(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Style(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "style";

    public new sealed record Props(
        string? Type = null,
        string? Media = null,
        string? Title = null,
        string? Nonce = null,
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

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Media is not null)
            {
                yield return new KeyValuePair<string, string?>("media", Media);
            }

            if (Title is not null)
            {
                yield return new KeyValuePair<string, string?>("title", Title);
            }

            if (Nonce is not null)
            {
                yield return new KeyValuePair<string, string?>("nonce", Nonce);
            }
        }
    }
}
