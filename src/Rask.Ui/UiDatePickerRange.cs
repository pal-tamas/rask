using System.Globalization;

namespace Rask.Ui;

/// <summary>
/// A field that shows a range of dates and opens a calendar to pick its two ends.
/// </summary>
/// <remarks>
/// Not written by name: <c>UiDatePicker</c> becomes this control when the model holds a <see cref="UiDateRange" /> —
/// <c>UiDatePicker.Bind(() =&gt; model.Stay)</c>, or <c>UiDatePicker.Value(new UiDateRange(from, to))</c> for the
/// parent to own. The first click is held and drawn as the start; the second writes the whole range and closes the
/// popover on the same click, as <see cref="UiCalendarRange" /> does without one.
/// </remarks>
[RaskChainEntry("UiDatePicker")]
public sealed partial class UiDatePickerRange : UiFormField<UiDateRange>
{
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);
    private DateOnly? _month;
    private DateOnly? _anchor;
    private bool _open;

    /// <summary>What the field shows before a range is chosen. "Choose dates" unless this says otherwise.</summary>
    public string? Placeholder { get; set; }

    /// <inheritdoc cref="UiCalendar.Min" />
    public DateOnly? Min { get; set; }

    /// <inheritdoc cref="UiCalendar.Max" />
    public DateOnly? Max { get; set; }

    /// <inheritdoc cref="UiCalendar.FirstDay" />
    public DayOfWeek? FirstDay { get; set; }

    // The open state, the half-picked start and the view month are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefix => "uidpr-" + _instance.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override Component Control()
    {
        var (acc, ctx, chosen) = UiFormCommit.Resolve<UiDateRange>(this);
        var panel = UiDayGrid.PanelIdOf(Prefix);
        var anchor = _anchor;

        var grid = UiDayGrid.Render(new UiDayGrid.View(
            Label ?? AccessibleLabel ?? "Dates",
            UiDayGrid.MonthOf(null, _month, anchor ?? (chosen == default ? null : chosen.Start)),
            FirstDay,
            Min,
            Max,
            "border-0",
            month =>
            {
                _month = month;
                return Task.CompletedTask;
            },
            date => UiCalendarRange.State(chosen, anchor, date),
            date =>
            {
                if (_anchor is not { } start)
                {
                    _anchor = date;
                    return Task.CompletedTask;
                }

                _anchor = null;
                return UiFormCommit.CommitAsync(this, acc, ctx, UiDateRange.Between(start, date));
            },
            // Only the click that gives the range its end closes the popover; the first one leaves it open for it.
            _ => anchor is null ? null : panel));

        return UiDayGrid.PickerShell(
            new UiDayGrid.Picker(
                Prefix,
                FieldId,
                chosen == default ? null : UiDayGrid.Short(chosen.Start) + " – " + UiDayGrid.Short(chosen.End),
                Placeholder ?? "Choose dates",
                ControlAria(),
                _open,
                Disabled == true,
                UiClass.Compose(UiDayGrid.BoxClass(Tone, Size, Variant), Class),
                Label ?? AccessibleLabel,
                open =>
                {
                    _open = open;
                    if (!open)
                    {
                        // Closed half-way through: the start is dropped with it, so reopening starts afresh.
                        _anchor = null;
                        _month = null;
                    }
                }),
            grid);
    }
}
