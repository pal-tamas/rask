namespace Rask.Core.Components;

public sealed class Track : Component
{
    protected override string TagName => "track";
    protected override bool SelfClosing => true;

    public string? Kind { get; set; }
    public string? Src { get; set; }
    public string? Srclang { get; set; }
    public string? Label { get; set; }
    public bool Default { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Kind is not null) yield return new("kind", Kind);
        if (Src is not null) yield return new("src", Src);
        if (Srclang is not null) yield return new("srclang", Srclang);
        if (Label is not null) yield return new("label", Label);
        if (Default) yield return new("default", null);
    }
}
