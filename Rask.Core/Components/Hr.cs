namespace Rask.Core.Components;

public sealed class Hr : Component<Hr.Props>
{
    public Hr(Props? props = null) : base(props, null) { }

    protected override string TagName => "hr";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
