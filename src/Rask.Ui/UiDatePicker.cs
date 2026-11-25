namespace Rask;

/// <summary>
/// A field that shows a date and opens a calendar to pick one — Flux UI's <c>flux:date-picker</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ui.DatePicker.Bind(() =&gt; model.Arrival)</c> two-way binds a <c>DateOnly</c> or a <c>DateOnly?</c>, a
/// <c>DateTime</c> or a <c>DateTime?</c> held as its day, or a <see cref="Rask.Values.Day" /> or
/// <see cref="Rask.Values.Moment" /> value object (<see cref="UiDatePickerMoments" />, <see cref="UiDatePickerDays" />);
/// <c>.Value(day).OnChange(…)</c> leaves a <c>DateOnly</c> with the parent. A pick closes the popup on the same click.
/// </para>
/// <para>
/// It is typed unless it says otherwise: the day typed in the culture's own order and separators (hu 2026. 11. 06.,
/// en-US 11/06/2026) or picked in the calendar, whose header chooses the month and the year.
/// <c>.Type(Ui.DatePickerType.Button)</c> is Flux's own default, a button showing the day as the locale writes a medium
/// date ("Jan 20, 2026"); <c>.Type(Ui.DatePickerType.Input)</c> is Flux's <c>type="input"</c>.
/// </para>
/// <para>
/// <c>Ui.DatePicker.Range</c> opens <see cref="UiDatePickerRange" /> over a <see cref="UiDateRange" />. Every prop is on
/// <see cref="UiDatePickerControl{T}" />.
/// </para>
/// </remarks>
public sealed partial class UiDatePicker : UiDatePickerControl<DateOnly>
{
    private protected override Ui.DatePickerMode Bound => Ui.DatePickerMode.Single;

    private protected override string DefaultPlaceholder => RaskStrings.Get(RaskString.DatePickerPlaceholder, "Select a date");

    private protected override string ConfirmLabel => RaskStrings.Get(RaskString.DatePickerConfirm, "Select date");

    private protected override UiCalendarPicks Picks(DateOnly current) => new UiCalendarSinglePick(current);

    private protected override string Written(DateOnly value) => UiCalendarValue.Day(value);

    private protected override bool TryRead(string text, out DateOnly value) => UiCalendarValue.TryDay(text, out value);

    private protected override string? Text(DateOnly current, UiCalendarOptions options) =>
        current == default ? null : UiDatePickerText.Medium(current, options.Culture);

    private protected override IReadOnlyList<DateOnly?> Dates(DateOnly current) => [current == default ? null : current];

    private protected override DateOnly Typed(DateOnly current, int slot, DateOnly date) => date;
}
