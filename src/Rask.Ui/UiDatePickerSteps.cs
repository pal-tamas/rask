namespace Rask;

/// <summary>The date picker's chain steps that take no value: <c>Ui.DatePicker.WithInputs()</c>.</summary>
/// <remarks>
///     Each hands its value to the generated step, which is what marks the prop written for this render: a step that
///     set the property itself would be reset at the render's end.
/// </remarks>
public static class UiDatePickerSteps
{
    /// <summary>Inputs over the months to type the date into, drawn as parts: Flux's <c>with-inputs</c>.</summary>
    /// <param name="picker">The picker.</param>
    /// <returns><paramref name="picker" />.</returns>
    public static UiDatePicker WithInputs(this UiDatePicker picker) => picker.WithInputs(Ui.DatePickerInputVariant.Custom);

    /// <summary>Inputs over the months to type the range's start and end into, drawn as parts: Flux's <c>with-inputs</c>.</summary>
    /// <param name="picker">The picker.</param>
    /// <returns><paramref name="picker" />.</returns>
    public static UiDatePickerRange WithInputs(this UiDatePickerRange picker) => picker.WithInputs(Ui.DatePickerInputVariant.Custom);
}
