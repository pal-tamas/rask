namespace Rask.Core.Components;

public sealed class Cite : Component<Cite.Props>
{
    public Cite(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Cite(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "cite";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
