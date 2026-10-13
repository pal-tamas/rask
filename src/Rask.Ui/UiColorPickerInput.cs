namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker.input</c>: the text input of a composed <see cref="UiColorPicker" />'s popover.
/// </summary>
public sealed partial class UiColorPickerInput : Component
{
    /// <summary>Shown while there is no colour. The picker's own placeholder when unset.</summary>
    public string? Placeholder { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Context.Get<UiColorPickerScope>()?.Picker.DrawInput(Placeholder);
}
