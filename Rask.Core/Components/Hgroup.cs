namespace Rask.Core.Components;

public sealed class Hgroup : Component<Hgroup.Props>
{
    public Hgroup(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Hgroup(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "hgroup";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
