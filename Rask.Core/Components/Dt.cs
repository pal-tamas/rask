namespace Rask.Core.Components;

public sealed class Dt : Component<Dt.Props>
{
    public Dt(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Dt(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "dt";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
