namespace Rask.Core.Components;

public sealed class S : Component<S.Props>
{
    public S(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public S(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "s";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
