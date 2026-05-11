namespace Rask.Core.Components;

public sealed class Strong : Component<Strong.Props>
{
    public Strong(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Strong(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "strong";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
