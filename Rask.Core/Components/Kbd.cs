namespace Rask.Core.Components;

public sealed class Kbd : Component<Kbd.Props>
{
    public Kbd(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Kbd(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "kbd";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
