namespace Rask.Core.Components;

public sealed class Noscript : Component<Noscript.Props>
{
    public Noscript(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Noscript(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "noscript";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
