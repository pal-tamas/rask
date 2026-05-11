namespace Rask.Core.Components;

public sealed class Template : Component<Template.Props>
{
    public Template(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Template(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "template";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
