namespace Rask.Core.Components;

public sealed class H1 : Component<H1.Props>
{
    public H1(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public H1(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "h1";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
