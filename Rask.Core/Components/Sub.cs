namespace Rask.Core.Components;

public sealed class Sub : Component<Sub.Props>
{
    public Sub(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Sub(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "sub";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
