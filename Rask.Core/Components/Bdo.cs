namespace Rask.Core.Components;

public sealed class Bdo : Element
{
    protected override string TagName => "bdo";

    public string? Dir { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Dir is not null) yield return new("dir", Dir);
    }
}
