namespace Rask.Core.Components;

public sealed class Tbody : Component<Tbody.Props>
{
    public Tbody(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Tbody(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "tbody";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
