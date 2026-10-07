namespace Rask;

/// <summary>
///     Flux's <c>flux:file-item.remove</c>: the small ✕ button for a <see cref="UiFileItem" />'s
///     <see cref="UiFileItem.Actions" />.
/// </summary>
/// <remarks>
///     It removes nothing itself: <see cref="OnClick" /> is where the page drops the file from its own state,
///     as Flux's examples do with <c>wire:click="removePhoto"</c>.
/// </remarks>
public sealed partial class UiFileItemRemove : Component
{
    private static readonly UiPartMarker Marker = new("ui-file-item-remove");

    /// <summary>Runs when the button is pressed — Rask's stand-in for <c>wire:click</c>.</summary>
    public Callback OnClick { get; set; }

    /// <summary>The button's accessible name. "Remove file" when unset; name the file in a list of several.</summary>
    public string? AriaLabel { get; set; }

    /// <summary>Classes for the call site, added to the button's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Button
            .Variant(Ui.ButtonVariant.Subtle)
            .Size(Ui.ButtonSize.Sm)
            .Square()
            .OnClick(OnClick)
            .AriaLabel(AriaLabel ?? "Remove file")
            .Data(Marker.With(null))
            .Class(UiClass.Compose("cursor-pointer", Class))[
            // A child, not the button's Icon: alone in a square that one is drawn at 20px, and Flux's is 16.
            Ui.Icon.Name(Ui.IconName.XMark).Micro
        ];
}
