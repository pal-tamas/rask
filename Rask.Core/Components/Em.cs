namespace Rask.Core.Components;

public sealed class Em : Component<Em.Props>
{
    public Em(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Em(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "em";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
