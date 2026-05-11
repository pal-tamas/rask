namespace Rask.Core.Components;

public sealed class H6 : Component<H6.Props>
{
    public H6(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public H6(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "h6";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
