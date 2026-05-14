namespace Rask.Core.Components;

public sealed class Ins : Element
{
    protected override string TagName => "ins";

    public string? Cite { get; set; }
    public string? DateTime { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Cite is not null) yield return new("cite", Cite);
        if (DateTime is not null) yield return new("datetime", DateTime);
    }
}
