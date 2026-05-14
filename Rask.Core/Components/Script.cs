namespace Rask.Core.Components;

public sealed class Script : Element
{
    protected override string TagName => "script";

    public string? Src { get; set; }
    public string? Type { get; set; }
    public bool Async { get; set; }
    public bool Defer { get; set; }
    public string? CrossOrigin { get; set; }
    public string? Integrity { get; set; }
    public bool NoModule { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? Charset { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Src is not null) yield return new("src", Src);
        if (Type is not null) yield return new("type", Type);
        if (Async) yield return new("async", null);
        if (Defer) yield return new("defer", null);
        if (CrossOrigin is not null) yield return new("crossorigin", CrossOrigin);
        if (Integrity is not null) yield return new("integrity", Integrity);
        if (NoModule) yield return new("nomodule", null);
        if (ReferrerPolicy is not null) yield return new("referrerpolicy", ReferrerPolicy);
        if (Charset is not null) yield return new("charset", Charset);
    }
}
