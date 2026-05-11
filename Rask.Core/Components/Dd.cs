namespace Rask.Core.Components;

public sealed class Dd : Component<Dd.Props>
{
    public Dd(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Dd(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "dd";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
