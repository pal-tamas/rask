namespace Rask.Core.Components;

public sealed class Pre : Component<Pre.Props>
{
    public Pre(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Pre(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "pre";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
