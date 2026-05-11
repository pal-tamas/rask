namespace Rask.Core.Components;

public sealed class Datalist : Component<Datalist.Props>
{
    public Datalist(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Datalist(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "datalist";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
