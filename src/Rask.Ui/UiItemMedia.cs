namespace Rask;

/// <summary>
///     What leads an item's row: an avatar, an icon, anything else. Flux UI's <c>item.media</c>.
/// </summary>
/// <remarks>
///     It sits at the start, 12px before the text, centred on the heading and subheading; several things sit side by
///     side, 12px apart. When the item's actions move under the text (<see cref="UiItem" />), it stays centred on the
///     heading and subheading alone.
/// </remarks>
public sealed partial class UiItemMedia : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-item-media");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "col-start-1 row-span-full self-center [:where(&)]:flex items-center [:where(&)]:gap-3 [:where(&)]:me-3 "
            + "@max-sm:[[data-ui-item]:has(>[data-ui-item-actions]_[data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))>&]:row-start-2 "
            + "@max-sm:[[data-ui-item]:has(>[data-ui-item-actions]_[data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))>&]:row-end-4",
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
