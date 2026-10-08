using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// What every calendar takes — Flux UI's <c>flux:calendar</c> props — whichever of the three it is:
/// <see cref="UiCalendar" /> for a day, <see cref="UiCalendarMultiple" /> for several,
/// <see cref="UiCalendarRange" /> for a range.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mode is the step that opens it.</b> <c>Ui.Calendar.Bind(() =&gt; m.Day)</c> is Flux's default, one day;
/// <c>Ui.Calendar.Multiple.Bind(() =&gt; m.Days)</c> (or <c>.Values([...])</c>) picks several over a collection, and
/// <c>Ui.Calendar.Range.Bind(() =&gt; m.Stay)</c> a range over a <see cref="UiDateRange" />. Each step hands back the
/// calendar that binds that mode's type, so a mismatch does not compile; <see cref="Mode" /> given as a value has
/// to agree with it.
/// </para>
/// <para>
/// The month it shows and where the keyboard is are the calendar's own state; the choice is the form's.
/// Unbound and with no <see cref="OnChange" /> answering, it keeps the choice itself, as Flux's does.
/// </para>
/// </remarks>
/// <typeparam name="T">What is chosen: a day, a collection of days, or a range.</typeparam>
public abstract partial class UiCalendarControl<T> : Component, IFormControl<T>, IUiClock
{
    private readonly UiCalendarState _state = new();
    private (bool Held, T? Value, T? Given) _own;

    /// <summary>
    ///     What is picked: a day, several days, or a range. The step that opens the calendar says it
    ///     (<c>Ui.Calendar.Range.Bind(…)</c>); set again on an opened calendar it has to agree with what is bound.
    /// </summary>
    public Ui.CalendarMode? Mode { get; set; }

    /// <summary>Flux's <c>multiple</c>: the same as <see cref="Ui.CalendarMode.Multiple" />.</summary>
    public bool? Multiple { get; set; }

    /// <summary>The earliest selectable day. Days before it are disabled.</summary>
    public DateOnly? Min { get; set; }

    /// <summary>The latest selectable day.</summary>
    public DateOnly? Max { get; set; }

    /// <summary>Days that cannot be selected: holidays, booked days.</summary>
    public IReadOnlyCollection<DateOnly>? Unavailable { get; set; }

    /// <summary>How large the day cells are.</summary>
    public Ui.CalendarSize? Size { get; set; }

    /// <summary>The day the week starts on. The locale's own unless set.</summary>
    public DayOfWeek? StartDay { get; set; }

    /// <summary>How many months are shown side by side: one, or two for a range, unless set.</summary>
    public int? Months { get; set; }

    /// <summary>The fewest days a range may hold.</summary>
    public int? MinRange { get; set; }

    /// <summary>The most days a range may hold.</summary>
    public int? MaxRange { get; set; }

    /// <summary>The day the calendar opens to while nothing is chosen.</summary>
    public DateOnly? OpenTo { get; set; }

    /// <summary>Opens to <see cref="OpenTo" /> whatever is chosen.</summary>
    public bool? ForceOpenTo { get; set; }

    /// <summary><c>false</c> hides the month steps.</summary>
    public bool? Navigation { get; set; }

    /// <summary>A calendar to look at: no day can be picked.</summary>
    public bool? Static { get; set; }

    /// <summary>Shows each week's number.</summary>
    public bool? WeekNumbers { get; set; }

    /// <summary>Makes the month and the year in the header selects.</summary>
    public bool? SelectableHeader { get; set; }

    /// <summary>Adds the shortcut that comes back to this month, and picks today once there.</summary>
    public bool? WithToday { get; set; }

    /// <summary>Always draws six weeks, so paging never changes the height.</summary>
    public bool? FixedWeeks { get; set; }

    /// <summary>The locale the names and the week follow (<c>fr</c>, <c>ja-JP</c>). The app's culture unless set.</summary>
    public string? Locale { get; set; }

    /// <summary>Extra classes for the calendar.</summary>
    public string? Class { get; set; }

    /// <summary>The calendar's id.</summary>
    public string? Id { get; set; }

    /// <inheritdoc />
    public T? Value { get; set; }

    /// <inheritdoc />
    public Callback<T> OnChange { get; set; }

    /// <inheritdoc />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc />
    public Callback<T> AfterBind { get; set; }

    /// <inheritdoc />
    DateOnly? IUiClock.Today { get; set; }

    // The view month and the cursor are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private protected UiCalendarState State => _state;

    /// <summary>The mode this calendar's bound type is.</summary>
    private protected abstract Ui.CalendarMode Bound { get; }

    /// <summary>Two months for a range, as Flux defaults.</summary>
    private protected virtual int DefaultMonths => 1;

    /// <inheritdoc />
    protected override Component? Render()
    {
        UiCalendarModes.Check(Multiple == true ? Ui.CalendarMode.Multiple : Mode, Bound);
        var (acc, ctx, bound) = UiFormCommit.Resolve<T>(this);
        var current = Bind is null && _own.Held && EqualityComparer<T?>.Default.Equals(Value, _own.Given) ? _own.Value : bound;

        return UiCalendarGrid.Render(Options(), _state, Picks(current, value =>
        {
            _own = (Bind is null, value, Value);
            return CommitAsync(acc, ctx, value);
        }));
    }

    /// <summary>What "chosen" means for this calendar, over the value as it stands.</summary>
    private protected abstract UiCalendarPicks Picks(T? current, Func<T, Task> commit);

    private protected virtual Task CommitAsync(ExpressionAccessor.Accessor? accessor, EditContext? context, T value) =>
        UiFormCommit.CommitAsync(this, accessor, context, value);

    /// <inheritdoc />
    protected override Task OnRendered() => UiCalendarFocus.MoveAsync(_state);

    private UiCalendarOptions Options()
    {
        var culture = Locale is { } locale ? CultureInfo.GetCultureInfo(locale) : CultureInfo.CurrentCulture;

        return new UiCalendarOptions(
            ((IUiClock)this).Today ?? UiCalendarOptions.Now(),
            culture,
            StartDay ?? culture.DateTimeFormat.FirstDayOfWeek,
            Size ?? Ui.CalendarSize.Base,
            Math.Max(1, Months ?? DefaultMonths))
        {
            Min = Min,
            Max = Max,
            Unavailable = Unavailable,
            MinRange = MinRange,
            MaxRange = MaxRange,
            OpenTo = OpenTo,
            ForceOpenTo = ForceOpenTo == true,
            Navigation = Navigation != false,
            Static = Static == true,
            WeekNumbers = WeekNumbers == true,
            SelectableHeader = SelectableHeader == true,
            WithToday = WithToday == true,
            FixedWeeks = FixedWeeks == true,
            Class = Class,
            Id = Id,
        };
    }
}
