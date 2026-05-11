namespace Rask.Core.Components;

public sealed class Small : Component<Small.Props>
{
    public Small(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Small(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "small";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
