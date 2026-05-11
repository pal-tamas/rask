namespace Rask.Core.Components;

public sealed class H4 : Component<H4.Props>
{
    public H4(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public H4(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "h4";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
