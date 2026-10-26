using System.Linq.Expressions;

namespace Rask;

/// <summary>
/// <c>Ui.DatetimePicker.Mode(mode)</c>: a datetime picker whose mode is a value. What it is opened with has to be that
/// mode's type — a mismatch is an <see cref="InvalidOperationException" /> at render.
/// </summary>
public readonly struct UiDatetimePickerModeEntry
{
    private readonly Ui.DatetimePickerMode _mode;

    internal UiDatetimePickerModeEntry(Ui.DatetimePickerMode mode) => _mode = mode;

    /// <summary>A moment the parent owns.</summary>
    /// <param name="value">The chosen moment.</param>
    public UiDatetimePicker Value(DateTime value) => Ui.DatetimePicker.Value(value).Mode(_mode);

    /// <summary>A range the parent owns.</summary>
    /// <param name="value">The chosen range.</param>
    public UiDatetimePickerRange Value(UiDateTimeRange value) => Ui.DatetimePicker.Range.Value(value).Mode(_mode);

    /// <summary>Binds a moment.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatetimePicker Bind(Expression<Func<DateTime>> bind) => Ui.DatetimePicker.Bind(bind).Mode(_mode);

    /// <summary>Binds a moment that may be unset.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatetimePicker Bind(Expression<Func<DateTime?>> bind) => Ui.DatetimePicker.Bind(bind).Mode(_mode);

    /// <summary>Binds a range.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatetimePickerRange Bind(Expression<Func<UiDateTimeRange>> bind) => Ui.DatetimePicker.Range.Bind(bind).Mode(_mode);

    /// <summary>Binds a range that may be unset.</summary>
    /// <param name="bind">The model's property.</param>
    public UiDatetimePickerRange Bind(Expression<Func<UiDateTimeRange?>> bind) => Ui.DatetimePicker.Range.Bind(bind).Mode(_mode);
}
