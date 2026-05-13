namespace Rask.Core.Components;

public sealed class Meta : Component
{
    protected override string TagName => "meta";
    protected override bool SelfClosing => true;

    public string? Charset { get; set; }
    public string? Name { get; set; }
    public string? Content { get; set; }
    public string? HttpEquiv { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Charset is not null) yield return new("charset", Charset);
        if (Name is not null) yield return new("name", Name);
        if (Content is not null) yield return new("content", Content);
        if (HttpEquiv is not null) yield return new("http-equiv", HttpEquiv);
    }
}
