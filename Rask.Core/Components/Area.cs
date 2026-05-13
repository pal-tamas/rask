namespace Rask.Core.Components;

public sealed class Area : Component
{
    protected override string TagName => "area";
    protected override bool SelfClosing => true;

    public string? Alt { get; set; }
    public string? Coords { get; set; }
    public string? Shape { get; set; }
    public string? Href { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }
    public string? Download { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Alt is not null) yield return new("alt", Alt);
        if (Coords is not null) yield return new("coords", Coords);
        if (Shape is not null) yield return new("shape", Shape);
        if (Href is not null) yield return new("href", Href);
        if (Target is not null) yield return new("target", Target);
        if (Rel is not null) yield return new("rel", Rel);
        if (Download is not null) yield return new("download", Download);
    }
}
