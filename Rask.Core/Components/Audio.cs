namespace Rask.Core.Components;

public sealed class Audio : Component
{
    protected override string TagName => "audio";

    public string? Src { get; set; }
    public bool Controls { get; set; }
    public bool Autoplay { get; set; }
    public bool Loop { get; set; }
    public bool Muted { get; set; }
    public string? Preload { get; set; }
    public string? CrossOrigin { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes())
        {
            yield return kv;
        }

        if (Src is not null) yield return new("src", Src);
        if (Controls) yield return new("controls", null);
        if (Autoplay) yield return new("autoplay", null);
        if (Loop) yield return new("loop", null);
        if (Muted) yield return new("muted", null);
        if (Preload is not null) yield return new("preload", Preload);
        if (CrossOrigin is not null) yield return new("crossorigin", CrossOrigin);
    }
}
