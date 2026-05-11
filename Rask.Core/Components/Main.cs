namespace Rask.Core.Components;

public sealed class Main : Component<Main.Props>
{
    public Main(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Main(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "main";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
