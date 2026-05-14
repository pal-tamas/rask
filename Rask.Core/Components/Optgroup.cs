namespace Rask.Core.Components;

public sealed class Optgroup : Element
{
    protected override string TagName => "optgroup";

    public bool Disabled { get; set; }
    public string? Label { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Disabled) yield return new("disabled", null);
        if (Label is not null) yield return new("label", Label);
    }
}
