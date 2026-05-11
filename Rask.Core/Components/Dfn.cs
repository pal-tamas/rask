namespace Rask.Core.Components;

public sealed class Dfn : Component<Dfn.Props>
{
    public Dfn(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Dfn(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "dfn";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
