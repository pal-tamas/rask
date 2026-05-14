namespace Rask.Core.Components;

public sealed class Style : Element
{
    protected override string TagName => "style";

    public string? Type { get; set; }
    public string? Media { get; set; }
    public string? Title { get; set; }
    public string? Nonce { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Type is not null) yield return new("type", Type);
        if (Media is not null) yield return new("media", Media);
        if (Title is not null) yield return new("title", Title);
        if (Nonce is not null) yield return new("nonce", Nonce);
    }
}
