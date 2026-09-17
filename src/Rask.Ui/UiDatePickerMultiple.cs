using System.Globalization;

namespace Rask.Ui;

/// <summary>
/// A field that shows several dates and opens a calendar to add or remove them.
/// </summary>
/// <remarks>
/// Not written by name: <c>UiDatePicker</c> becomes this control when the model holds a collection of days —
/// <c>UiDatePicker.Bind(() =&gt; model.DaysOff)</c>, or <c>UiDatePicker.Values([...])</c> for the parent to own.
/// The popover stays OPEN on a pick, because choosing four days should not mean opening it four times; Escape or a
/// click outside closes it.
/// </remarks>
[RaskChainEntry("UiDatePicker")]
public sealed partial class UiDatePickerMultiple : UiFormField<ICollection<DateOnly>>
{
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);
    private DateOnly? _month;
    private bool _open;

    /// <summary>What the field shows before a day is chosen. "Choose dates" unless this says otherwise.</summary>
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

    private string Prefix => "uidpm-" + _instance.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override Component Control()
    {
        var (acc, ctx, current) = UiFormCommit.Resolve<ICollection<DateOnly>>(this);
        var chosen = current ?? [];

        var grid = UiDayGrid.Render(new UiDayGrid.View(
            Label ?? AccessibleLabel ?? "Dates",
            UiDayGrid.MonthOf(null, _month, chosen.Count > 0 ? chosen.Min() : null),
            FirstDay,
            Min,
            Max,
            "border-0",
            month =>
            {
                _month = month;
                return Task.CompletedTask;
            },
            date => new UiDayGrid.DayState(chosen.Contains(date), false),
            date => UiFormCommit.CommitSelectionAsync(this, acc, ctx, UiCalendarMultiple.Toggle(chosen, date)),
            static _ => null));

        return UiDayGrid.PickerShell(
            new UiDayGrid.Picker(
                Prefix,
                FieldId,
                Text(chosen),
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
                        _month = null;
                    }
                }),
            grid);
    }

    // Every day while there are few enough to read; past three, the first two and how many more — a field that
    // wraps onto a second line to list a fortnight is not a field any more.
    internal static string? Text(ICollection<DateOnly> chosen)
    {
        if (chosen.Count == 0)
        {
            return null;
        }

        var days = chosen.Order().Select(UiDayGrid.Short).ToList();
        return days.Count <= 3
            ? string.Join(", ", days)
            : days[0] + ", " + days[1] + ", +" + (days.Count - 2).ToString(CultureInfo.CurrentCulture);
    }
}
