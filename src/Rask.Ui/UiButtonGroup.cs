namespace Rask;

/// <summary>
///     Flux's <c>flux:button.group</c>: the buttons inside it fused into one control, with one border between
///     each pair and corners only at the two ends.
/// </summary>
/// <remarks>
///     <c>Ui.ButtonGroup[Ui.Button["Oldest"], Ui.Button["Newest"], Ui.Button["Top"]]</c>. The buttons draw
///     the fusing themselves, from being inside one; ghost and subtle buttons have no surface to fuse.
/// </remarks>
public sealed partial class UiButtonGroup : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-button-group");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("flex", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?>? ResolveData() => Marker.With(Data);
}
