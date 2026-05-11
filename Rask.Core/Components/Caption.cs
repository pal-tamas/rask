namespace Rask.Core.Components;

public sealed class Caption : Component<Caption.Props>
{
    public Caption(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Caption(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "caption";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
