namespace Rask.Core.Components;

public sealed class Menu : Component<Menu.Props>
{
    public Menu(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Menu(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "menu";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
