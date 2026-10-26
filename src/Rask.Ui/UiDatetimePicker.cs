namespace Rask;

/// <summary>
/// A field that shows a date and a time and opens a calendar with the times beside it — Flux UI's
/// <c>flux:datetime-picker</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ui.DatetimePicker.Bind(() =&gt; model.StartsAt)</c> two-way binds a <c>DateTime</c> (or a <c>DateTime?</c>);
/// <c>.Value(moment).OnChange(…)</c> leaves it with the parent. The button reads "Oct 27, 2026 at 10:30 AM".
/// </para>
/// <para>
/// <c>Ui.DatetimePicker.Range</c> opens <see cref="UiDatetimePickerRange" /> over a <see cref="UiDateTimeRange" />. Every
/// prop is on <see cref="UiDatetimePickerControl{T}" />.
/// </para>
/// </remarks>
public sealed partial class UiDatetimePicker : UiDatetimePickerControl<DateTime>
{
    private protected override Ui.DatetimePickerMode Bound => Ui.DatetimePickerMode.Single;

    private protected override string DefaultPlaceholder => RaskStrings.Get(RaskString.DatetimePickerPlaceholder, "Select a date and time");

    private protected override string ConfirmLabel => RaskStrings.Get(RaskString.DatetimePickerSelect, "Select");

    private protected override UiCalendarPicks Picks(DateTime current) => new UiCalendarSinglePick(current == default ? default : DateOnly.FromDateTime(current));

    private protected override (DateOnly? Day, TimeOnly? Time)[] Ends(DateTime current) =>
        [current == default ? (null, null) : (DateOnly.FromDateTime(current), new TimeOnly(current.Hour, current.Minute))];

    private protected override string Written(DateTime value) => UiDatetimePickerValue.Moment(value);

    private protected override bool TryRead(string text, out DateTime value) => UiDatetimePickerValue.TryMoment(text, out value);

    private protected override (string Before, string Between, string After)? Text(DateTime current, UiDatetimeTimes times, DateOnly today) =>
        current == default ? null : UiDatetimePickerText.Moment(current, times.Pattern, times.Culture);
}
