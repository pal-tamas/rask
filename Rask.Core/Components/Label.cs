namespace Rask.Core.Components;

public sealed class Label : Component
{
    protected override string TagName => "label";

    public string? For { get; set; }
    public string? Form { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (For is not null) yield return new("for", For);
        if (Form is not null) yield return new("form", Form);
    }
}
