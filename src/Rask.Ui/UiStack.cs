namespace Rask.Ui;

/// <summary>
/// Elements stacked on top of one another.
/// </summary>
public sealed partial class UiStack : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("stack", Class))[Children ?? []];
}
