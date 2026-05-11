namespace Rask.Core.Components;

public sealed class Code : Component<Code.Props>
{
    public Code(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Code(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "code";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
