namespace Rask.Core.Components;

public sealed class Data : Component
{
    protected override string TagName => "data";

    public string? Value { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Value is not null) yield return new("value", Value);
    }
}
