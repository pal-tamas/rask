using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// What every datetime picker takes — Flux UI's <c>flux:datetime-picker</c> props — whichever of the two it is:
/// <see cref="UiDatetimePicker" /> for one date and time, <see cref="UiDatetimePickerRange" /> for a start and an end.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mode is the step that opens it</b>, as for the date picker: <c>Ui.DatetimePicker</c> binds a <c>DateTime</c>,
/// <c>Ui.DatetimePicker.Range</c> binds a <see cref="UiDateTimeRange" />. Wall-clock times, as a <c>datetime-local</c>
/// field holds them: <c>2026-10-14T10:30</c> is half past ten that day, and nothing converts it between time zones —
/// convert to and from a reader's zone yourself.
/// </para>
/// <para>
/// A field that shows the choice and opens the date picker's calendar with the times beside it. Picking a time
/// closes it; changing only the day writes the value at once. A range, typed times (<c>.Time(Ui.DatetimePickerTime.Input)</c>),
/// <c>WithConfirmation()</c> and every picker on a phone wait for the Select button instead. Everything a reader does
/// in the popup is the browser's (<c>rask-calendar.ts</c>): the page hears the value.
/// </para>
/// </remarks>
/// <typeparam name="T">What is chosen: a moment or a range.</typeparam>
public abstract partial class UiDatetimePickerControl<T> : Component, IFormControl<T>, IUiFormControl, IUiClock, IDeferredBind
{
    private static readonly UiPartMarker FocusPlaceholder = new("ui-focus-placeholder");

    private static readonly Dictionary<string, string?> PopupMarks = new(StringComparer.Ordinal)
    {
        ["rask-lock"] = "",
        // Under the trigger's start, five pixels away; the stylesheet's own sheet on a phone (see UiDatePickerLook.Dialog).
        ["rask-place"] = "bottom start 5 0",
    };

    private static readonly UiPartMarker SelectMarker = new("ui-datetime-picker-select");

    private readonly string _popover = UiDatetimePickerIds.Next();
    private string? _ownId;
    private (bool Held, T? Value, T? Given) _own;

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <inheritdoc cref="IUiFormControl.Badge" />
    public string? Badge { get; set; }

    /// <summary>What the button shows while nothing is chosen.</summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     What is picked: a moment or a range. The step that opens the picker says it
    ///     (<c>Ui.DatetimePicker.Range.Bind(…)</c>); set again on an opened picker it has to agree with what is bound.
    /// </summary>
    public Ui.DatetimePickerMode? Mode { get; set; }

    /// <summary>How large the trigger and the calendar's day cells are.</summary>
    public Ui.DatetimePickerSize? Size { get; set; }

    /// <summary>How times are chosen: from a list, or typed.</summary>
    public Ui.DatetimePickerTime? Time { get; set; }

    /// <summary>Minutes between the times in the list. 30 unless set.</summary>
    public int? Interval { get; set; }

    /// <summary>12-hour or 24-hour. The locale's own unless set.</summary>
    public Ui.DatetimePickerTimeFormat? TimeFormat { get; set; }

    /// <summary>The earliest moment that can be picked: days before it are disabled, and so are the earlier times of its day.</summary>
    public DateTime? Min { get; set; }

    /// <summary>The latest moment that can be picked: days after it are disabled, and so are the later times of its day.</summary>
    public DateTime? Max { get; set; }

    /// <summary>The earliest time of day listed, on every day.</summary>
    public TimeOnly? MinTime { get; set; }

    /// <summary>The latest time of day listed, on every day.</summary>
    public TimeOnly? MaxTime { get; set; }

    /// <summary>Days, times of every day, and times of one day that cannot be picked.</summary>
    public IReadOnlyList<UiDatetimeSlot>? Unavailable { get; set; }

    /// <summary>
    ///     The time filled in as soon as a day is picked; for a range, the start's and the end's:
    ///     <c>.DefaultTime(new TimeOnly(9, 0))</c>, <c>.DefaultTime(new UiTimeRange(new(9, 0), new(17, 0)))</c>.
    /// </summary>
    public UiTimeRange? DefaultTime { get; set; }

    /// <summary>The fewest days a range may span, its ends included.</summary>
    public int? MinRange { get; set; }

    /// <summary>The most days a range may span, its ends included.</summary>
    public int? MaxRange { get; set; }

    /// <summary>How many months are shown side by side: one, or two for a range, unless set.</summary>
    public int? Months { get; set; }

    /// <summary>Holds the choice until the reader presses Select. Always so for a range and for typed times.</summary>
    public bool? WithConfirmation { get; set; }

    /// <summary>A button in the trigger that empties the picker, shown while something is chosen.</summary>
    public bool? Clearable { get; set; }

    /// <summary>Adds the shortcut that comes back to this month, and picks today once there.</summary>
    public bool? WithToday { get; set; }

    /// <summary>Makes the month and the year in the calendar's header selects.</summary>
    public bool? SelectableHeader { get; set; }

    /// <summary>Shows each week's number.</summary>
    public bool? WeekNumbers { get; set; }

    /// <summary>The day the week starts on. The locale's own unless set.</summary>
    public DayOfWeek? StartDay { get; set; }

    /// <summary>Always draws six weeks, so paging never changes the height.</summary>
    public bool? FixedWeeks { get; set; }

    /// <summary>
    ///     Where the picker opens while nothing is chosen — a moment, a day, or a time of today:
    ///     <c>.OpenTo(new DateTime(2026, 12, 1, 9, 0, 0))</c>, <c>.OpenTo(new TimeOnly(9, 0))</c>.
    /// </summary>
    public UiDatetimeSlot? OpenTo { get; set; }

    /// <summary>The locale the names, the week, the dates and the clock follow. The app's culture unless set.</summary>
    public string? Locale { get; set; }

    /// <summary>Prevents the reader from opening the picker.</summary>
    public bool? Disabled { get; set; }

    /// <inheritdoc cref="IUiFormControl.Invalid" />
    public bool? Invalid { get; set; }

    /// <summary>What opens the picker instead of its own button: a <c>Ui.Button</c>, a <c>Ui.DatetimePickerButton</c>.</summary>
    public Component? Trigger { get; set; }

    /// <summary>Extra classes for the picker.</summary>
    public string? Class { get; set; }

    /// <summary>The trigger's id; derived from the binding or the label unless set.</summary>
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

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

    /// <summary>
    ///     Writes the model as soon as it is chosen, and validates then: <c>.Live()</c>, Flux's
    ///     <c>wire:model.live</c>. Without it the choice is written with the next action, and travels with that.
    /// </summary>
    public bool? Live { get; set; }

    /// <inheritdoc />
    DateOnly? IUiClock.Today { get; set; }

    /// <summary>The time "now" is, for the list a picker with nothing chosen opens on; the system clock's while unset.</summary>
    internal TimeOnly? Now { get; set; }

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    // A choice the picker keeps itself is a FIELD, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <summary>Two months for a range, as Flux defaults.</summary>
    private protected virtual int DefaultMonths => 1;

    /// <summary>The mode this picker's bound type is.</summary>
    private protected abstract Ui.DatetimePickerMode Bound { get; }

    private protected abstract string DefaultPlaceholder { get; }

    private protected abstract string ConfirmLabel { get; }

    // A range and typed times always wait for Select: they have no natural last step.
    private bool Confirms => WithConfirmation == true || Time == Ui.DatetimePickerTime.Input || Bound == Ui.DatetimePickerMode.Range;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        UiDatetimePickerModes.Check(Mode, Bound);
        var (acc, ctx, bound) = UiFormCommit.Resolve<T>(this);
        var committed = Bind is null && _own.Held && EqualityComparer<T?>.Default.Equals(Value, _own.Given) ? _own.Value : bound;
        var culture = Locale is { } locale ? CultureInfo.GetCultureInfo(locale) : CultureInfo.CurrentCulture;
        var options = Options(culture);
        var times = Times(culture);
        var ends = Ends(committed);

        Task Commit(T value)
        {
            _own = (Bind is null, value, Value);
            return UiFormCommit.CommitAsync(this, acc, ctx, value);
        }

        var scope = new UiDatetimePickerScope
        {
            PopoverId = _popover,
            ControlId = field.ControlId,
            Placeholder = Placeholder ?? DefaultPlaceholder,
            Text = Text(committed, times, options.Today),
            Disabled = Disabled == true,
            Invalid = field.Invalid,
            Aria = field.Aria,
            Clearable = Clearable == true,
            Clear = () => ClearAsync(acc, ctx),
        };

        var config = UiCalendarHook.Config(options, Picks(committed).Mode, new UiCalendarHook.Picker(Confirms, scope.Placeholder), json => Write(json, times, options.Culture));

        return field.Wrap(
            Div.Class(UiClass.Compose(RootClass, Class))
                .Data(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ui-control"] = "",
                    ["ui-datetime-picker"] = "",
                    ["rask-calendar"] = config,
                    [UiInvoker.OpenMark] = _popover,
                })
                .Attributes(("style", "anchor-name:--" + _popover))[
                FieldOf(committed, Commit),
                Context.Provide(scope)[OwnedTrigger()],
                Popup(options, times, committed, ends)
            ]);
    }

    private string? RootClass
    {
        get
        {
            if (Trigger is not (null or UiDatetimePickerButton))
            {
                return null;
            }

            return Bound == Ui.DatetimePickerMode.Range ? UiDatetimePickerLook.RootRange : UiDatetimePickerLook.Root;
        }
    }

    // The one field the page hears the choice through: on change, or as it is chosen for a bound picker that says
    // `.Live()`; bound and waiting, it goes ahead of the reader's next action.
    private HTMLInputElement<string> FieldOf(T? committed, Func<T, Task> commit)
    {
        var input = Input.Value(Written(committed)).Type(InputType.Hidden);
        if (Bind is null && !OnChange.HasValue)
        {
            return input;
        }

        Task Changed(string text) => TryRead(text, out var value) ? commit(value) : Task.CompletedTask;

        if (Bind is not null && Live == true)
        {
            return input.OnInput(Changed);
        }

        input = input.OnChange(Changed);
        return UiDraws.Waits(this, Bind is not null) ? input.Attributes(UiCalendarHook.WaitsForAction) : input;
    }

    // Nothing chosen: null where the bound member can say so, the type's unset value where it cannot.
    private Task ClearAsync(ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        _own = (Bind is null, default, Value);
        return UiFormCommit.ClearAsync(this, accessor, context);
    }

    // The caller's own trigger opens the popup as Flux's does: its button names the dialog it opens.
    private Component OwnedTrigger() => Trigger switch
    {
        null => Ui.DatetimePickerButton,
        Element element => UiInvoker.Decorate(element, _popover, "dialog", false),
        var other => other,
    };

    private Component Popup(UiCalendarOptions options, UiDatetimeTimes times, T? committed, (DateOnly? Day, TimeOnly? Time)[] ends)
    {
        var range = Bound == Ui.DatetimePickerMode.Range;
        var listed = !times.Typed && !range;

        return Dialog
            .Id(_popover)
            .Class(UiDatePickerLook.Dialog)
            .Popover(Rask.Core.Popover.Auto)
            .Data(PopupMarks)
            // Flux focuses nothing when its picker opens: `autofocus` on the dialog keeps the focus on the popup.
            .Attributes(("autofocus", ""), ("style", "position-anchor:--" + _popover))[
            Div.Class(UiDatePickerLook.FocusPlaceholder).Data(FocusPlaceholder.With(null)).TabIndex(0),
            Div.Class(UiDatetimePickerLook.Body)[
                UiCalendarGrid.Render(options with { Id = _popover + "-calendar", InPopup = true }, Picks(committed), UiDatetimePickerLook.Calendar, marked: false),
                listed ? UiDatetimePickerTimes.Column(times, ends[0].Day, ends[0].Time) : null
            ],
            range ? RangeFields(times, ends) : TimeField(times, ends, listed),
            Footer(range, committed)
        ];
    }

    // A range's start and end, each with its own time field.
    private static Component RangeFields(UiDatetimeTimes times, (DateOnly? Day, TimeOnly? Time)[] ends) =>
        Div.Class(UiDatetimePickerLook.Ends)[
            Div.Class(UiDatetimePickerLook.End)[
                Div.Class(UiDatetimePickerLook.RowLabel)[RaskStrings.Get(RaskString.DatetimePickerStart, "Start")],
                UiDatetimePickerTimes.Field(times, 0, "start", ends[0].Day, ends[0].Time)
            ],
            Div.Class(UiDatetimePickerLook.EndBelow)[
                Div.Class(UiDatetimePickerLook.RowLabel)[RaskStrings.Get(RaskString.DatetimePickerEnd, "End")],
                UiDatetimePickerTimes.Field(times, 1, "end", ends[1].Day, ends[1].Time)
            ]
        ];

    // A moment's time field under the calendar: the phone's in place of the column, and typed times' everywhere.
    private static Component TimeField(UiDatetimeTimes times, (DateOnly? Day, TimeOnly? Time)[] ends, bool listed) =>
        Div.Class(listed ? UiDatetimePickerLook.RowPhone : UiDatetimePickerLook.Row)[
            Div.Class(UiDatetimePickerLook.RowLabel)[RaskStrings.Get(RaskString.DatetimePickerTime, "Time")],
            UiDatetimePickerTimes.Field(times, 0, "single", ends[0].Day, ends[0].Time)
        ];

    // Cancel and Select — Select range for a range — with a range's message while its end is not after its start.
    // Shown where the choice waits for Select, and on every phone.
    private Component Footer(bool range, T? committed) =>
        Div.Class(Confirms ? UiDatetimePickerLook.Footer : UiDatetimePickerLook.FooterPhone)[
            range
                ? Div.Class(UiDatetimePickerLook.Error).Data("ui-datetime-picker-error", "").Attributes(("hidden", ""), UiCalendarHook.Named("error"))[
                    RaskStrings.Get(RaskString.DatetimePickerEndBeforeStart, "End must be after start")
                ]
                : null,
            Div.Class("inline")[
                Ui.Button.Ghost.CommandFor(_popover).Command("hide-popover")[RaskStrings.Get(RaskString.DatePickerCancel, "Cancel")]
            ],
            Ui.Button.Primary.CommandFor(_popover).Command("hide-popover")
                .Disabled(Written(committed).Length == 0)
                .Data(SelectMarker.With(null))
                .Attributes(UiCalendarHook.Named("confirm"))[ConfirmLabel]
        ];

    // What the browser needs to draw and rule the times (rask-calendar.ts).
    private void Write(Utf8JsonWriter json, UiDatetimeTimes times, CultureInfo culture)
    {
        json.WriteStartObject("time");
        json.WriteBoolean("input", times.Typed);
        json.WriteString("pattern", times.Pattern);
        json.WriteString("am", UiTimePickerLook.Period(false, culture));
        json.WriteString("pm", UiTimePickerLook.Period(true, culture));
        json.WriteString("at", UiDatetimePickerText.At(culture));
        json.WriteString("atClass", UiDatetimePickerLook.At);
        json.WriteString("wordsClass", UiDatetimePickerLook.Words);
        json.WriteString("emptyClass", UiDatetimePickerLook.Placeholder);
        json.WriteString("placeholderClass", UiTimePickerLook.Placeholder);
        json.WriteString("monthDay", UiDatetimePickerText.MonthDayPattern(culture));
        json.WriteString("comma", UiDatetimePickerText.Comma);
        json.WriteString("placeholder", RaskStrings.Get(RaskString.DatetimePickerTime, "Time"));
        if (Min is { } min)
        {
            json.WriteString("min", UiDatetimePickerValue.Moment(min));
        }

        if (Max is { } max)
        {
            json.WriteString("max", UiDatetimePickerValue.Moment(max));
        }

        json.WriteStartArray("unavailable");
        foreach (var slot in times.Unavailable.Where(slot => slot.From is not null))
        {
            json.WriteStringValue(slot.ToString());
        }

        json.WriteEndArray();
        if (DefaultTime is { } defaults)
        {
            json.WriteStartArray("defaults");
            json.WriteNumberValue(UiDatetimePickerValue.Minutes(defaults.Start));
            json.WriteNumberValue(UiDatetimePickerValue.Minutes(defaults.End));
            json.WriteEndArray();
        }

        if (OpenTo?.From is { } openTo)
        {
            json.WriteNumber("openTo", UiDatetimePickerValue.Minutes(openTo));
        }

        json.WriteEndObject();
    }

    private UiDatetimeTimes Times(CultureInfo culture) => new(
        _popover,
        UiTimePickerLook.Times(MinTime, MaxTime, Interval ?? 30),
        UiDatetimePickerText.TimePattern(TimeFormat ?? Ui.DatetimePickerTimeFormat.Auto, culture),
        culture,
        Time == Ui.DatetimePickerTime.Input,
        Disabled == true,
        Min,
        Max,
        Unavailable ?? [],
        OpenTo?.From,
        Now ?? TimeOnly.FromDateTime(UiCalendarOptions.Clock.GetLocalNow().DateTime));

    private UiCalendarOptions Options(CultureInfo culture) =>
        new(
            ((IUiClock)this).Today ?? UiCalendarOptions.Now(),
            culture,
            StartDay ?? culture.DateTimeFormat.FirstDayOfWeek,
            UiDatetimePickerLook.Cells(Size),
            Math.Max(1, Months ?? DefaultMonths))
        {
            Min = Min is { } min ? DateOnly.FromDateTime(min) : null,
            Max = Max is { } max ? DateOnly.FromDateTime(max) : null,
            Unavailable = Unavailable?.Where(slot => slot is { Date: not null, From: null }).Select(slot => slot.Date!.Value).ToList(),
            MinRange = MinRange,
            MaxRange = MaxRange,
            OpenTo = OpenTo?.Date,
            WeekNumbers = WeekNumbers == true,
            SelectableHeader = SelectableHeader == true,
            WithToday = WithToday == true,
            FixedWeeks = FixedWeeks == true,
        };

    /// <summary>What "chosen" means for the calendar, over the value as it stands.</summary>
    private protected abstract UiCalendarPicks Picks(T? current);

    /// <summary>The day and time of each end: one for a moment, a range's start and end.</summary>
    private protected abstract (DateOnly? Day, TimeOnly? Time)[] Ends(T? current);

    /// <summary>The value as the picker's field carries it (<see cref="UiDatetimePickerValue" />).</summary>
    private protected abstract string Written(T? value);

    /// <summary>Reads what the browser announced; false for text that is no value of this picker.</summary>
    private protected abstract bool TryRead(string text, out T value);

    /// <summary>What the button shows for <paramref name="current" />; nothing while it is unset.</summary>
    private protected abstract (string Before, string Between, string After)? Text(T? current, UiDatetimeTimes times, DateOnly today);
}
