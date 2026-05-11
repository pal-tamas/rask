namespace Rask.Core.Components;

public sealed class Audio : Component<Audio.Props>
{
    public Audio(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Audio(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "audio";

    public new sealed record Props(
        string? Src = null,
        bool Controls = false,
        bool Autoplay = false,
        bool Loop = false,
        bool Muted = false,
        string? Preload = null,
        string? CrossOrigin = null,
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

            if (Controls)
            {
                yield return new KeyValuePair<string, string?>("controls", null);
            }

            if (Autoplay)
            {
                yield return new KeyValuePair<string, string?>("autoplay", null);
            }

            if (Loop)
            {
                yield return new KeyValuePair<string, string?>("loop", null);
            }

            if (Muted)
            {
                yield return new KeyValuePair<string, string?>("muted", null);
            }

            if (Preload is not null)
            {
                yield return new KeyValuePair<string, string?>("preload", Preload);
            }

            if (CrossOrigin is not null)
            {
                yield return new KeyValuePair<string, string?>("crossorigin", CrossOrigin);
            }
        }
    }
}
