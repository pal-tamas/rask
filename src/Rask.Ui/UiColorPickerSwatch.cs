namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker.swatch</c>: one colour, written as a child of <c>Ui.ColorPickerSwatches</c>.
/// </summary>
public sealed partial class UiColorPickerSwatch : Component
{
    /// <summary>The colour, as a CSS colour string: <c>#ef4444</c>.</summary>
    public required string Value { get; set; }

    /// <summary>What assistive tech calls the swatch. The value itself when unset.</summary>
    public string? Label { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Context.Get<UiColorPickerScope>()?.Picker.DrawSwatches([new UiColorSwatch(Value, Label)], null);
}
