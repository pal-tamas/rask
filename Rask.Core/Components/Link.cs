namespace Rask.Core.Components;

public sealed class Link : Element
{
    protected override string TagName => "link";
    protected override bool SelfClosing => true;

    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Type { get; set; }
    public string? Media { get; set; }
    public string? Sizes { get; set; }
    public string? Hreflang { get; set; }
    public string? As { get; set; }
    public string? CrossOrigin { get; set; }
    public string? ReferrerPolicy { get; set; }
    public bool Disabled { get; set; }
    public string? Color { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Href is not null) yield return new("href", Href);
        if (Rel is not null) yield return new("rel", Rel);
        if (Type is not null) yield return new("type", Type);
        if (Media is not null) yield return new("media", Media);
        if (Sizes is not null) yield return new("sizes", Sizes);
        if (Hreflang is not null) yield return new("hreflang", Hreflang);
        if (As is not null) yield return new("as", As);
        if (CrossOrigin is not null) yield return new("crossorigin", CrossOrigin);
        if (ReferrerPolicy is not null) yield return new("referrerpolicy", ReferrerPolicy);
        if (Disabled) yield return new("disabled", null);
        if (Color is not null) yield return new("color", Color);
    }
}
