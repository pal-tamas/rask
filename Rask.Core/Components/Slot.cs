namespace Rask.Core.Components;

public sealed class Slot : Element
{
    protected override string TagName => "slot";

    public string? Name { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Name is not null) yield return new("name", Name);
    }
}
