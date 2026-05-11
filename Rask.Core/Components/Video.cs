using System.Globalization;

namespace Rask.Core.Components;

public sealed class Video : Component<Video.Props>
{
    public Video(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Video(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "video";

    public new sealed record Props(
        string? Src = null,
        string? Poster = null,
        int? Width = null,
        int? Height = null,
        bool Controls = false,
        bool Autoplay = false,
        bool Loop = false,
        bool Muted = false,
        string? Preload = null,
        string? CrossOrigin = null,
        bool PlaysInline = false,
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

            if (Poster is not null)
            {
                yield return new KeyValuePair<string, string?>("poster", Poster);
            }

            if (Width is not null)
            {
                yield return new KeyValuePair<string, string?>("width",
                    Width.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Height is not null)
            {
                yield return new KeyValuePair<string, string?>("height",
                    Height.Value.ToString(CultureInfo.InvariantCulture));
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

            if (PlaysInline)
            {
                yield return new KeyValuePair<string, string?>("playsinline", null);
            }
        }
    }
}
