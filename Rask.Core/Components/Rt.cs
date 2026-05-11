namespace Rask.Core.Components;

public sealed class Rt : Component<Rt.Props>
{
    public Rt(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Rt(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "rt";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
