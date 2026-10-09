namespace Rask;

/// <summary>
/// A field that shows a date and opens a calendar to pick one — Flux UI's <c>flux:date-picker</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ui.DatePicker.Bind(() =&gt; model.Arrival)</c> two-way binds a <c>DateOnly</c> (or a <c>DateOnly?</c>);
/// <c>.Value(day).OnChange(…)</c> leaves it with the parent. The button shows the day as the locale writes a
/// medium date ("Jan 20, 2026"), and a pick closes the popup on the same click.
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

    private protected override UiCalendarPicks Picks(DateOnly current, Func<DateOnly, Task> choose) =>
        new UiCalendarSinglePick(current, choose);

    private protected override string? Text(DateOnly current, UiCalendarOptions options) =>
        current == default ? null : Medium(current, options.Culture);

    private protected override IReadOnlyList<DateOnly?> Dates(DateOnly current) => [current == default ? null : current];

    private protected override DateOnly Typed(DateOnly current, int slot, DateOnly date) => date;
}
