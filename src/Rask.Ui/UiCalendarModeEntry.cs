using System.Linq.Expressions;

namespace Rask;

/// <summary>
/// <c>Ui.Calendar.Mode(mode)</c>: a calendar whose mode is a value. It opens like any calendar, and what it is
/// opened with has to be that mode's type — a mismatch is an <see cref="InvalidOperationException" /> at render.
/// </summary>
public readonly struct UiCalendarModeEntry
{
    private readonly Ui.CalendarMode _mode;

    internal UiCalendarModeEntry(Ui.CalendarMode mode) => _mode = mode;

    /// <summary>A day the parent owns.</summary>
    /// <param name="value">The chosen day.</param>
    public UiCalendar Value(DateOnly value) => Ui.Calendar.Value(value).Mode(_mode);

    /// <summary>A range the parent owns.</summary>
    /// <param name="value">The chosen range.</param>
    public UiCalendarRange Value(UiDateRange value) => Ui.Calendar.Range.Value(value).Mode(_mode);

    /// <summary>Days the parent owns.</summary>
    /// <param name="values">The chosen days.</param>
    public UiCalendarMultiple Values(ICollection<DateOnly>? values) => Ui.Calendar.Multiple.Values(values).Mode(_mode);

    /// <summary>Binds a day.</summary>
    /// <param name="bind">The model's property.</param>
    public UiCalendar Bind(Expression<Func<DateOnly>> bind) => Ui.Calendar.Bind(bind).Mode(_mode);

    /// <summary>Binds a day that may be unset.</summary>
    /// <param name="bind">The model's property.</param>
    public UiCalendar Bind(Expression<Func<DateOnly?>> bind) => Ui.Calendar.Bind(bind).Mode(_mode);

    /// <summary>Binds a range.</summary>
    /// <param name="bind">The model's property.</param>
    public UiCalendarRange Bind(Expression<Func<UiDateRange>> bind) => Ui.Calendar.Range.Bind(bind).Mode(_mode);

    /// <summary>Binds a range that may be unset.</summary>
    /// <param name="bind">The model's property.</param>
    public UiCalendarRange Bind(Expression<Func<UiDateRange?>> bind) => Ui.Calendar.Range.Bind(bind).Mode(_mode);

    /// <summary>Binds a list of days.</summary>
    /// <param name="bind">The model's property.</param>
    public UiCalendarMultiple Bind(Expression<Func<List<DateOnly>>> bind) => Ui.Calendar.Multiple.Bind(bind).Mode(_mode);

    /// <summary>Binds a collection of days.</summary>
    /// <param name="bind">The model's property.</param>
    public UiCalendarMultiple Bind(Expression<Func<ICollection<DateOnly>>> bind) => Ui.Calendar.Multiple.Bind(bind).Mode(_mode);
}
