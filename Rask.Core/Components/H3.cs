namespace Rask.Core.Components;

public sealed class H3 : Component<H3.Props>
{
    public H3(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public H3(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "h3";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
