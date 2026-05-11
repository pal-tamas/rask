namespace Rask.Core.Components;

public sealed class Head : Component<Head.Props>
{
    public Head(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Head(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "head";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
