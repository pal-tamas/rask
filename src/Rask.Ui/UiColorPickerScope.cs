namespace Rask;

/// <summary>What a <see cref="UiColorPicker" /> tells the parts a page composed inside it.</summary>
/// <remarks>
///     The parts are written by the caller, anywhere under the picker's children, so there is no call site at
///     which to hand them the picker. A new scope is provided every render: that is what draws them again.
/// </remarks>
/// <param name="Picker">The picker the part belongs to, which draws it and owns its handlers.</param>
/// <param name="Render">Which render of the picker this is.</param>
internal sealed record UiColorPickerScope(UiColorPicker Picker, int Render);
