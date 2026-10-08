using System.Linq.Expressions;

namespace Rask;

/// <summary>
/// <c>Ui.DatePicker.Mode(mode)</c>: a date picker whose mode is a value. It opens like any picker, and what it is
/// opened with has to be that mode's type — a mismatch is an <see cref="InvalidOperationException" /> at render.
/// </summary>
public readonly struct UiDatePickerModeEntry
{
    private readonly Ui.DatePickerMode _mode;

    internal UiDatePickerModeEntry(Ui.DatePickerMode mode) => _mode = mode;

    /// <summary>A day the parent owns.</summary>
    /// <param name="value">The chosen day.</param>
    public UiDatePicker Value(DateOnly value) => Ui.DatePicker.Value(value).Mode(_mode);

    /// <summary>A range the parent owns.</summary>
    /// <param name="value">The chosen range.</param>
    public UiDatePickerRange Value(UiDateRange value) => Ui.DatePicker.Range.Value(value).Mode(_mode);

    /// <summary>Binds a day.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatePicker Bind(Expression<Func<DateOnly>> bind) => Ui.DatePicker.Bind(bind).Mode(_mode);

    /// <summary>Binds a day that may be unset.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatePicker Bind(Expression<Func<DateOnly?>> bind) => Ui.DatePicker.Bind(bind).Mode(_mode);

    /// <summary>Binds a range.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatePickerRange Bind(Expression<Func<UiDateRange>> bind) => Ui.DatePicker.Range.Bind(bind).Mode(_mode);

    /// <summary>Binds a range that may be unset.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatePickerRange Bind(Expression<Func<UiDateRange?>> bind) => Ui.DatePicker.Range.Bind(bind).Mode(_mode);
}
