namespace Rask.Core.Components;

public sealed class Html : Component<Html.Props>
{
    public Html(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Html(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "html";

    public new sealed record Props(
        string? Lang = null,
        string? Dir = null,
        string? Xmlns = null,
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

            if (Lang is not null)
            {
                yield return new KeyValuePair<string, string?>("lang", Lang);
            }

            if (Dir is not null)
            {
                yield return new KeyValuePair<string, string?>("dir", Dir);
            }

            if (Xmlns is not null)
            {
                yield return new KeyValuePair<string, string?>("xmlns", Xmlns);
            }
        }
    }
}
