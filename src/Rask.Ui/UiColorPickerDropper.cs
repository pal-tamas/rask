namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker.dropper</c>: a button of a composed <see cref="UiColorPicker" /> that picks
///     a colour from anywhere on the screen, through the browser's EyeDropper.
/// </summary>
public sealed partial class UiColorPickerDropper : Component
{
    /// <summary>Classes added to the button's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Context.Get<UiColorPickerScope>()?.Picker.DrawDropper(Class);
}
