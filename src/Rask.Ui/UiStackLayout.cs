namespace Rask;

/// <summary>
/// Elements stacked on top of one another.
/// </summary>
[RaskChainGroup(typeof(Ui), "Stack")]
public sealed partial class UiStackLayout : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("stack", Class))[Children ?? []];
}
