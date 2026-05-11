namespace Rask.Core.Components;

public sealed class Table : Component<Table.Props>
{
    public Table(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Table(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "table";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
