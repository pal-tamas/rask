namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker.area</c>: the saturation and brightness area of a composed
///     <see cref="UiColorPicker" />.
/// </summary>
public sealed partial class UiColorPickerArea : Component
{
    /// <summary>Classes added to the area's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Context.Get<UiColorPickerScope>()?.Picker.DrawArea(Class);
}
