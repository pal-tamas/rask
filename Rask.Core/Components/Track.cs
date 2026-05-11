namespace Rask.Core.Components;

public sealed class Track : Component<Track.Props>
{
    public Track(Props? props = null) : base(props, null) { }

    protected override string TagName => "track";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Kind = null,
        string? Src = null,
        string? Srclang = null,
        string? Label = null,
        bool Default = false,
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

            if (Kind is not null)
            {
                yield return new KeyValuePair<string, string?>("kind", Kind);
            }

            if (Src is not null)
            {
                yield return new KeyValuePair<string, string?>("src", Src);
            }

            if (Srclang is not null)
            {
                yield return new KeyValuePair<string, string?>("srclang", Srclang);
            }

            if (Label is not null)
            {
                yield return new KeyValuePair<string, string?>("label", Label);
            }

            if (Default)
            {
                yield return new KeyValuePair<string, string?>("default", null);
            }
        }
    }
}
