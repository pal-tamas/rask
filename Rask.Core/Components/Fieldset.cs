namespace Rask.Core.Components;

public sealed class Fieldset : Element
{
    protected override string TagName => "fieldset";

    public bool Disabled { get; set; }
    public string? Form { get; set; }
    public string? Name { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Disabled) yield return new("disabled", null);
        if (Form is not null) yield return new("form", Form);
        if (Name is not null) yield return new("name", Name);
    }
}
