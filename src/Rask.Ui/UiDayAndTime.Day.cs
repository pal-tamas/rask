namespace Rask;

// A moment asked as its day alone: a DateTime the member's shape says is a day — [DataType(DataType.Date)], or a
// FieldKind.Day shape — which Ui.Field.Bind draws as one date picker, as it draws a DateOnly. It is the pair's day without
// the time: a new day keeps the time of day the moment had, seconds and its Kind too (a moment held as a day is
// midnight, so it stays one), and a first day is midnight. No day is no moment, as for the pair.
public sealed partial class UiDayAndTime<T>
{
    // What `.DayAlone(…)` asked, kept as `.BindParts(…)` is: no prop holds it.
    private UiDayAlone? _dayAlone;

    /// <summary>Draws the day alone, with <paramref name="placeholder" /> on its button.</summary>
    internal UiDayAndTime<T> DayAlone(string? placeholder)
    {
        _dayAlone = new UiDayAlone(placeholder);
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    // The day picker as a bound DateOnly's is drawn: a button over the calendar, its placeholder, the row's classes.
    private UiDatePicker DayAlone(UiDatePicker day, UiDayAlone alone) => day.Placeholder(alone.Placeholder).Class(Class);

    private sealed record UiDayAlone(string? Placeholder);
}
