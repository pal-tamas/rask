namespace Rask.Core.Components;

public sealed class Blockquote : Component
{
    protected override string TagName => "blockquote";

    public string? Cite { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Cite is not null) yield return new("cite", Cite);
    }
}
