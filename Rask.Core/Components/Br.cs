namespace Rask.Core.Components;

public sealed class Br : Component<Br.Props>
{
    public Br(Props? props = null) : base(props, null) { }

    protected override string TagName => "br";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
