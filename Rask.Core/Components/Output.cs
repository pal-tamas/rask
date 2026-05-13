namespace Rask.Core.Components;

public sealed class Output : Component
{
    protected override string TagName => "output";

    public string? For { get; set; }
    public string? Form { get; set; }
    public string? Name { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (For is not null) yield return new("for", For);
        if (Form is not null) yield return new("form", Form);
        if (Name is not null) yield return new("name", Name);
    }
}
