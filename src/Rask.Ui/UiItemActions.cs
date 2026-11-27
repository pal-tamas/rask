namespace Rask;

/// <summary>
///     Buttons, a switch or a value at the end of an item's row. Flux UI's <c>item.actions</c>.
/// </summary>
/// <remarks>
///     16px after the text, centred on the heading and subheading; several things sit side by side, 8px apart. On an
///     item under 24rem of its own width, actions holding a button with words move under the text, 12px below it, so the
///     heading keeps its room; a switch, a value, a menu's button or an icon button stays at the end (measured on
///     fluxui.dev: a <see cref="UiButton" /> moves them unless it is square or inside a <see cref="UiDropdown" />).
/// </remarks>
public sealed partial class UiItemActions : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-item-actions");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "col-start-3 row-span-full self-center justify-self-end [:where(&)]:flex items-center [:where(&)]:gap-2 [:where(&)]:ms-4 "
            + "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:col-[2/-1] "
            + "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:row-start-4 "
            + "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:row-end-auto "
            + "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:justify-self-start "
            + "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:ms-0 "
            + "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:mt-3",
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
