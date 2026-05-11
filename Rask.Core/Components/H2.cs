namespace Rask.Core.Components;

public sealed class H2 : Component<H2.Props>
{
    public H2(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public H2(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "h2";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
