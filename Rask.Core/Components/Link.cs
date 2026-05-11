namespace Rask.Core.Components;

public sealed class Link : Component<Link.Props>
{
    public Link(Props? props = null) : base(props, null) { }

    protected override string TagName => "link";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Href = null,
        string? Rel = null,
        string? Type = null,
        string? Media = null,
        string? Sizes = null,
        string? Hreflang = null,
        string? As = null,
        string? CrossOrigin = null,
        string? ReferrerPolicy = null,
        bool Disabled = false,
        string? Color = null,
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

            if (Href is not null)
            {
                yield return new KeyValuePair<string, string?>("href", Href);
            }

            if (Rel is not null)
            {
                yield return new KeyValuePair<string, string?>("rel", Rel);
            }

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Media is not null)
            {
                yield return new KeyValuePair<string, string?>("media", Media);
            }

            if (Sizes is not null)
            {
                yield return new KeyValuePair<string, string?>("sizes", Sizes);
            }

            if (Hreflang is not null)
            {
                yield return new KeyValuePair<string, string?>("hreflang", Hreflang);
            }

            if (As is not null)
            {
                yield return new KeyValuePair<string, string?>("as", As);
            }

            if (CrossOrigin is not null)
            {
                yield return new KeyValuePair<string, string?>("crossorigin", CrossOrigin);
            }

            if (ReferrerPolicy is not null)
            {
                yield return new KeyValuePair<string, string?>("referrerpolicy", ReferrerPolicy);
            }

            if (Disabled)
            {
                yield return new KeyValuePair<string, string?>("disabled", null);
            }

            if (Color is not null)
            {
                yield return new KeyValuePair<string, string?>("color", Color);
            }
        }
    }
}
