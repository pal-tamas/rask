using System.Globalization;

namespace Rask.Core.Components;

public sealed class Video : Component
{
    protected override string TagName => "video";

    public string? Src { get; set; }
    public string? Poster { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool Controls { get; set; }
    public bool Autoplay { get; set; }
    public bool Loop { get; set; }
    public bool Muted { get; set; }
    public string? Preload { get; set; }
    public string? CrossOrigin { get; set; }
    public bool PlaysInline { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Src is not null) yield return new("src", Src);
        if (Poster is not null) yield return new("poster", Poster);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));
        if (Controls) yield return new("controls", null);
        if (Autoplay) yield return new("autoplay", null);
        if (Loop) yield return new("loop", null);
        if (Muted) yield return new("muted", null);
        if (Preload is not null) yield return new("preload", Preload);
        if (CrossOrigin is not null) yield return new("crossorigin", CrossOrigin);
        if (PlaysInline) yield return new("playsinline", null);
    }
}
