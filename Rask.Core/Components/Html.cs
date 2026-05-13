namespace Rask.Core.Components;

public sealed class Html : Component
{
    protected override string TagName => "html";

    public string? Lang { get; set; }
    public string? Dir { get; set; }
    public string? Xmlns { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Lang is not null) yield return new("lang", Lang);
        if (Dir is not null) yield return new("dir", Dir);
        if (Xmlns is not null) yield return new("xmlns", Xmlns);
    }
}
