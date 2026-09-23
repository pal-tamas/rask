using System.Globalization;

namespace Rask;

/// <summary>
/// A field that shows a date and opens a calendar to pick one.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's date picker: a field-shaped button showing the chosen day in the reader's own short date format, and
/// the month grid in a popover beside it. The browser owns the popover — the top layer, Escape, a click outside,
/// and focus back on the button — and a pick closes it on the same click.
/// </para>
/// <para>
/// A form field like the kit's inputs, so <c>Label</c>, <c>Hint</c>, <c>Error</c>, <c>Badge</c> and validation all
/// work as they do there. <b>The same entry picks several days or a range</b>: bind a collection of days and it is
/// <see cref="UiDatePickerMultiple" />, which stays open while days are added; bind a <see cref="UiDateRange" />
/// and it is <see cref="UiDatePickerRange" />, which closes once the range has both ends.
/// </para>
/// <para>
/// Nothing here is typed. Where a date may be far away, a <c>UiInput</c> of type date beside it — or instead of
/// it — is faster than paging through months, and the only route for somebody who cannot use a pointer comfortably.
/// </para>
/// </remarks>
public sealed partial class UiDatePicker : UiFormField<DateOnly>
{
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);
    private DateOnly? _month;
    private bool _open;

    /// <summary>What the field shows before a day is chosen. "Choose a date" unless this says otherwise.</summary>
    public string? Placeholder { get; set; }

    /// <inheritdoc cref="UiCalendar.Min" />
    public DateOnly? Min { get; set; }

    /// <inheritdoc cref="UiCalendar.Max" />
    public DateOnly? Max { get; set; }

    /// <inheritdoc cref="UiCalendar.FirstDay" />
    public DayOfWeek? FirstDay { get; set; }

    // The open state and the view month are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefix => "uidp-" + _instance.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override Component Control()
    {
        var (acc, ctx, chosen) = UiFormCommit.Resolve<DateOnly>(this);
        var panel = UiDayGrid.PanelIdOf(Prefix);

        var grid = UiDayGrid.Render(new UiDayGrid.View(
            Label ?? AccessibleLabel ?? "Date",
            UiDayGrid.MonthOf(null, _month, chosen),
            FirstDay,
            Min,
            Max,
            "border-0",
            month =>
            {
                _month = month;
                return Task.CompletedTask;
            },
            date => new UiDayGrid.DayState(chosen == date, false),
            date => UiFormCommit.CommitAsync(this, acc, ctx, date),
            _ => panel));

        return UiDayGrid.PickerShell(
            new UiDayGrid.Picker(
                Prefix,
                FieldId,
                chosen == default ? null : UiDayGrid.Short(chosen),
                Placeholder ?? "Choose a date",
                ControlAria(),
                _open,
                Disabled == true,
                UiClass.Compose(UiDayGrid.BoxClass(Tone, Size, Variant), Class),
                Label ?? AccessibleLabel,
                open =>
                {
                    _open = open;
                    // Reopening goes back to the chosen day's month rather than wherever it was last paged to.
                    if (!open)
                    {
                        _month = null;
                    }
                }),
            grid);
    }
}
