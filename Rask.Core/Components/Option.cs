namespace Rask.Core.Components;

public sealed class Option : Component
{
    protected override string TagName => "option";

    public string? Value { get; set; }
    public bool Selected { get; set; }
    public bool Disabled { get; set; }
    public string? Label { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Value is not null) yield return new("value", Value);
        if (Selected) yield return new("selected", null);
        if (Disabled) yield return new("disabled", null);
        if (Label is not null) yield return new("label", Label);
    }
}
