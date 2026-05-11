namespace Rask.Core.Components;

public sealed class Title : Component<Title.Props>
{
    public Title(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Title(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "title";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
