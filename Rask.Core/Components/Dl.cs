namespace Rask.Core.Components;

public sealed class Dl : Component<Dl.Props>
{
    public Dl(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Dl(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "dl";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
