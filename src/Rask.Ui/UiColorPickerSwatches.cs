namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker.swatches</c>: the palette of a composed <see cref="UiColorPicker" />.
/// </summary>
/// <remarks>
///     The picker's own swatches unless <c>Ui.ColorPickerSwatch</c> children say which.
/// </remarks>
public sealed partial class UiColorPickerSwatches : Component
{
    /// <summary>Classes added to the container's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiColorPickerScope>() is not { } scope)
        {
            return null;
        }

        var own = (Children ?? []).OfType<UiColorPickerSwatch>().Select(swatch => new UiColorSwatch(swatch.Value, swatch.Label)).ToList();

        return scope.Picker.DrawSwatches(own.Count > 0 ? own : null, Class);
    }
}
