namespace Rask.Core.Components;

public sealed class U : Component<U.Props>
{
    public U(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public U(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "u";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
