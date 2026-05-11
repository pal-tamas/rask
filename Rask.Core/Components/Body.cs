namespace Rask.Core.Components;

public sealed class Body : Component<Body.Props>
{
    public Body(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Body(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "body";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
