using static Rask.Markup;

namespace Rask;

/// <summary>The date picker's chain steps that take no value: <c>Ui.DatePicker.WithInputs()</c>, <c>Ui.DatePicker.Typed()</c>.</summary>
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

    /// <summary>
    ///     The day typed in the culture's own order and separators (hu: 2026. 11. 06., en-US: 11/06/2026) or picked in
    ///     the calendar, whose header chooses the month and the year: Flux's <c>type="input"</c> with
    ///     <c>selectable-header</c>. The culture is the picker's <c>Locale</c>, the current culture unless set.
    /// </summary>
    /// <param name="picker">The picker.</param>
    /// <returns><paramref name="picker" />.</returns>
    public static UiDatePicker Typed(this UiDatePicker picker) => picker.Type(Ui.DatePickerType.Input).SelectableHeader(true);

    /// <summary>
    ///     The range typed as two fields side by side, its start and its end — labelled from the kit's catalog — or
    ///     picked in the calendar, whose header chooses the month and the year.
    /// </summary>
    /// <param name="picker">The picker.</param>
    /// <returns><paramref name="picker" />.</returns>
    public static UiDatePickerRange Typed(this UiDatePickerRange picker) =>
        picker.SelectableHeader(true).Trigger(Div.Class("flex gap-4")[
            Ui.DatePickerInput.Label(RaskStrings.Get(RaskString.DatetimePickerStart, "Start")),
            Ui.DatePickerInput.Label(RaskStrings.Get(RaskString.DatetimePickerEnd, "End"))
        ]);
}
