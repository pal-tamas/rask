namespace Rask.Ui;

/// <summary>
/// Rows of information, each a small grid.
/// </summary>
public sealed partial class UiList : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose("list rounded-box bg-base-100", Class))[Children ?? []];
}
