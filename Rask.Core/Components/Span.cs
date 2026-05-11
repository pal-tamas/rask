namespace Rask.Core.Components;

public sealed class Span : Component<Span.Props>
{
    public Span(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Span(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "span";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
