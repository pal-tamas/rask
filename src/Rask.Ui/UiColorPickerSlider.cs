namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker.slider</c>: the hue or the opacity slider of a composed
///     <see cref="UiColorPicker" />.
/// </summary>
public sealed partial class UiColorPickerSlider : Component
{
    /// <summary>What the slider moves. The hue when unset.</summary>
    public Ui.ColorPickerChannel? Channel { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Context.Get<UiColorPickerScope>()?.Picker.DrawSlider(Channel ?? Ui.ColorPickerChannel.Hue);
}
