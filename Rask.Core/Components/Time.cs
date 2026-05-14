namespace Rask.Core.Components;

public sealed class Time : Element
{
    protected override string TagName => "time";

    public string? DateTime { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (DateTime is not null) yield return new("datetime", DateTime);
    }
}
