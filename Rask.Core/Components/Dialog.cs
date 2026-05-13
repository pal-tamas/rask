namespace Rask.Core.Components;

public sealed class Dialog : Component
{
    protected override string TagName => "dialog";

    public bool Open { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Open) yield return new("open", null);
    }
}
