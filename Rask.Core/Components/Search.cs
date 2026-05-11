namespace Rask.Core.Components;

public sealed class Search : Component<Search.Props>
{
    public Search(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Search(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "search";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
