namespace Rask.Core.Components;

public sealed class Base : Component
{
    protected override string TagName => "base";
    protected override bool SelfClosing => true;

    public string? Href { get; set; }
    public string? Target { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Href is not null) yield return new("href", Href);
        if (Target is not null) yield return new("target", Target);
    }
}
