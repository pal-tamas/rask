namespace Rask.Core.Components;

public sealed class Legend : Component<Legend.Props>
{
    public Legend(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Legend(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "legend";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
