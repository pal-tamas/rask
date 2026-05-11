namespace Rask.Core.Components;

public sealed class Nav : Component<Nav.Props>
{
    public Nav(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Nav(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "nav";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
