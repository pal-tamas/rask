namespace Rask.Core.Components;

public sealed class Footer : Component<Footer.Props>
{
    public Footer(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Footer(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "footer";

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
