namespace Rask.Core.Components;

public sealed class Source : Component<Source.Props>
{
    public Source(Props? props = null) : base(props, null) { }

    protected override string TagName => "source";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Src = null,
        string? Type = null,
        string? Srcset = null,
        string? Sizes = null,
        string? Media = null,
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

            if (Src is not null)
            {
                yield return new KeyValuePair<string, string?>("src", Src);
            }

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Srcset is not null)
            {
                yield return new KeyValuePair<string, string?>("srcset", Srcset);
            }

            if (Sizes is not null)
            {
                yield return new KeyValuePair<string, string?>("sizes", Sizes);
            }

            if (Media is not null)
            {
                yield return new KeyValuePair<string, string?>("media", Media);
            }
        }
    }
}
