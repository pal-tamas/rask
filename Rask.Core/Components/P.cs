namespace Rask.Core.Components;

public sealed class P : Component<P.Props>
{
    public P(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public P(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "p";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
