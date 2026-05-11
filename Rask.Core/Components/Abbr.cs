namespace Rask.Core.Components;

public sealed class Abbr : Component<Abbr.Props>
{
    public Abbr(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Abbr(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "abbr";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
