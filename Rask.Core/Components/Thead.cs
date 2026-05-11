namespace Rask.Core.Components;

public sealed class Thead : Component<Thead.Props>
{
    public Thead(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Thead(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "thead";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
