namespace Rask.Core.Components;

public sealed class Source : Element
{
    protected override string TagName => "source";
    protected override bool SelfClosing => true;

    public string? Src { get; set; }
    public string? Type { get; set; }
    public string? Srcset { get; set; }
    public string? Sizes { get; set; }
    public string? Media { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Src is not null) yield return new("src", Src);
        if (Type is not null) yield return new("type", Type);
        if (Srcset is not null) yield return new("srcset", Srcset);
        if (Sizes is not null) yield return new("sizes", Sizes);
        if (Media is not null) yield return new("media", Media);
    }
}
