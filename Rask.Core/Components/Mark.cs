namespace Rask.Core.Components;

public sealed class Mark : Component<Mark.Props>
{
    public Mark(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Mark(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "mark";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
