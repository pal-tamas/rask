namespace Rask.Core.Components;

public sealed class Tr : Component<Tr.Props>
{
    public Tr(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Tr(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "tr";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
