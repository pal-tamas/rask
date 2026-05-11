namespace Rask.Core.Components;

public sealed class Sup : Component<Sup.Props>
{
    public Sup(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Sup(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "sup";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
