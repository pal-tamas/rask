namespace Rask.Ui;

/// <summary>
/// A bar of destinations pinned to the bottom of the viewport, for a phone.
/// </summary>
/// <remarks>
/// The mobile counterpart to <see cref="UiNavbar" />: thumbs reach the bottom of a phone screen and not
/// the top. It is fixed to the viewport, so a page using one needs padding at its end or the last row
/// sits underneath it.
/// </remarks>
public sealed partial class UiDock : Component
{
    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "dock",
            Size is { } size ? UiClassNames.DockSize(size) : "",
            Class))[Children ?? []];
}
