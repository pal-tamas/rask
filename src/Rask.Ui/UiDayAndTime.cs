using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A moment asked as a day and a time of that day in one row: <c>Ui.DayAndTime.Bind(() =&gt; m.StartsAt)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Binds ONE <c>DateTime?</c> (or <c>DateTime</c>). The day is <c>Ui.DatePicker</c> typed (<c>.Typed()</c>: typed in
/// the culture's order, or picked in its calendar), the time <c>Ui.TimePicker</c> typed beside it — Flux's own two
/// pickers, a day and a time as Flux puts them side by side, not the one-button <c>Ui.DatetimePicker</c>. Where the
/// row has no room for both, as on a phone, the time goes beneath the day.
/// </para>
/// <para>
/// No day is no moment: the time is disabled until a day is given, and a day taken away takes its time with it. A
/// first day is its first minute. A new day keeps the time the moment had; a new time keeps its day. The time field
/// writes hours and minutes: a time left at the hour and the minute the value already shows keeps the value
/// whole, seconds and all — a save never loses seconds nobody changed — and any other time has none.
/// </para>
/// <para>
/// Whether it may be left empty is the bound member's, as for <c>Ui.Select</c>: a <c>DateTime?</c> the model does not
/// require has the day's × and empties to <see langword="null" />; a required <c>DateTime?</c> has no × and, emptied,
/// says so at the save; a <c>DateTime</c> cannot be empty, and an emptied day says it must be given at once. A day or
/// a time half typed says so under the pair, as each picker does, and the save is refused. Every message is the
/// kit's catalog's or the model's own rules' (<c>Validate</c>, attributes), under the pair in the member's name.
/// </para>
/// </remarks>
/// <typeparam name="T"><c>DateTime</c> or <c>DateTime?</c>.</typeparam>
public sealed partial class UiDayAndTime<T> : Component, IFormControl<T>, IUiFormControl, IDeferredBind, IUiLockable
{
    // The day takes the room, never less than its whole date and its clear button; the time needs a third of it, and
    // room for AM/PM where the culture's clock has twelve hours.
    private const string Row = "[:where(&)]:flex [:where(&)]:flex-wrap [:where(&)]:gap-2";
    private const string DayWidth = "min-w-48 flex-1";
    private const string TimeWidth = "w-36 shrink-0";
    private const string TimeWidthTwelve = "w-44 shrink-0";

    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal) { ["ui-day-and-time"] = "" };

    private string? _ownId;

    // The binding as the last render resolved it: a handler reads the moment through it as it is when it runs.
    private ExpressionAccessor.Accessor? _accessor;
    private EditContext? _context;
    private T? _shown;

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <inheritdoc cref="IUiFormControl.Badge" />
    public string? Badge { get; set; }

    /// <summary>Takes no input, the day nor the time.</summary>
    public bool? Disabled { get; set; }

    /// <inheritdoc />
    bool IUiLockable.Locked => Disabled == true;

    /// <inheritdoc cref="IUiFormControl.Invalid" />
    public bool? Invalid { get; set; }

    /// <summary>The culture the day is typed and the time written in. The current culture unless set.</summary>
    public string? Locale { get; set; }

    /// <summary>Classes for the row: widths and margins.</summary>
    public string? Class { get; set; }

    /// <summary>The day's id, which the label points at; derived from the binding or the label unless set.</summary>
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T> AfterBind { get; set; }

    /// <summary>
    ///     Writes the model as soon as the day or the time is given, and validates then:
    ///     <c>Ui.DayAndTime.Bind(() =&gt; m.At).Live()</c>. Without it the moment is written with the next action.
    /// </summary>
    public bool? Live { get; set; }

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    // The day and the time are drawn from a moment the session may still hold for the next action.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        UiDayAndTimeValue.Check<T>();
        var field = UiWithField.For(this);
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        (_accessor, _context, _shown) = (accessor, context, current);
        var moment = UiDayAndTimeValue.Read(current);
        var locked = Disabled == true;

        var day = Ui.DatePicker
            .Value(UiDayAndTimeValue.Day(moment) ?? default)
            .OnChange(DayAsync)
            .Typed()
            .Key("day")
            .Id(field.ControlId)
            .Locale(Locale)
            .Clearable(UiDayAndTimeValue.MayBeEmpty<T>(Bind))
            .Disabled(locked)
            .Invalid(field.Invalid)
            .Class(DayWidth);
        var time = Ui.TimePicker
            .Value(UiDayAndTimeValue.Time(moment))
            .OnChange(TimeAsync)
            .Type(Ui.TimePickerType.Input)
            .Key("time")
            .Id(field.ControlId + "-time")
            .Locale(Locale)
            .Disabled(locked || moment is null)
            .Invalid(field.Invalid)
            .Class(UiTimePickerLook.TwelveHour(Ui.TimePickerTimeFormat.Auto, Culture) ? TimeWidthTwelve : TimeWidth);

        // What either cannot take is said under the pair, in the bound member's name.
        return field.Wrap(Div.Class(Row, Class).Data(Marks)[Context.Provide(new UiUnreadRelay(UnreadAsync))[day, time]]);
    }

    private CultureInfo Culture => Locale is { } name ? CultureInfo.GetCultureInfo(name) : CultureInfo.CurrentCulture;

    // The moment as it is now: the model's, or what the session holds for it.
    private DateTime? Now() => UiDayAndTimeValue.Read(_accessor is { } bound ? UiFormCommit.Shown(this, bound) : _shown);

    private Task DayAsync(DateOnly day)
    {
        if (UiDayAndTimeValue.OnDay(Now(), day == default ? null : day) is { } moment)
        {
            return UiFormCommit.CommitAsync(this, _accessor, _context, UiDayAndTimeValue.As<T>(moment));
        }

        // The day taken away: no moment where the bound type can say so — a required one says it must be given at
        // the save. A DateTime cannot: the field says so at once, and keeps the moment it had.
        return typeof(T) == typeof(DateTime?) ? UiFormCommit.ClearAsync(this, _accessor, _context) : UnreadAsync(Rules.Required);
    }

    private Task TimeAsync(TimeOnly? time) =>
        UiDayAndTimeValue.AtTime(Now(), time) is { } moment
            ? UiFormCommit.CommitAsync(this, _accessor, _context, UiDayAndTimeValue.As<T>(moment))
            : Task.CompletedTask;

    private Task UnreadAsync(string said) => UiFormCommit.UnreadAsync(this, _accessor, _context, _shown, said);
}
