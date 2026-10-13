using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// What every date picker takes — Flux UI's <c>flux:date-picker</c> props — whichever of the two it is:
/// <see cref="UiDatePicker" /> for a day, <see cref="UiDatePickerRange" /> for a range.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mode is the step that opens it</b>, as for <see cref="UiCalendarControl{T}" />: <c>Ui.DatePicker</c> binds a
/// <c>DateOnly</c> and picks a day, <c>Ui.DatePicker.Range</c> binds a <see cref="UiDateRange" /> and picks a range.
/// </para>
/// <para>
/// A field that shows the choice and a calendar in a popup beside it. The popup is the platform's own — a
/// <c>popover</c>, so the browser owns the top layer, Escape, a click outside and handing focus back — and a
/// pick that finishes the choice closes it on the same click. A form control like the kit's inputs:
/// <see cref="Label" />, <see cref="Description" /> and validation work as they do there.
/// </para>
/// </remarks>
/// <typeparam name="T">What is chosen: a day or a range.</typeparam>
public abstract partial class UiDatePickerControl<T> : Component, IFormControl<T>, IUiFormControl, IUiClock, IDeferredBind
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["ui-control"] = "",
        ["ui-date-picker"] = "",
    };

    private static readonly Dictionary<string, string?> MarksOpen = new(Marks, StringComparer.Ordinal) { ["open"] = "" };

    private static readonly UiPartMarker FocusPlaceholder = new("ui-focus-placeholder");

    private static readonly Dictionary<string, string?> PopupClosed = new(StringComparer.Ordinal)
    {
        ["rask-popover-open"] = "false",
        ["rask-lock"] = "",
        // Under the trigger's start, five pixels away: the runtime keeps it inside the viewport from this.
        ["rask-place"] = "bottom start 5 0",
    };

    private static readonly Dictionary<string, string?> PopupOpen = new(PopupClosed, StringComparer.Ordinal) { ["rask-popover-open"] = "true" };

    private readonly string _popover = UiDatePickerIds.Next();
    private readonly UiCalendarState _state = new();
    private string? _ownId;
    private (bool Held, T? Value, T? Given) _own;
    private (bool Held, T? Value) _pending;

    // The typed fields the reader emptied, a bit a slot: a range is nothing chosen once both are.
    private int _emptied;
    private bool _open;

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <inheritdoc cref="IUiFormControl.Badge" />
    public string? Badge { get; set; }

    /// <summary>What opens the picker: a button showing the choice, or a field to type it into.</summary>
    public Ui.DatePickerType? Type { get; set; }

    /// <summary>What the trigger shows while nothing is chosen.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The earliest selectable day; also where the "All Time" preset starts.</summary>
    public DateOnly? Min { get; set; }

    /// <summary>The latest selectable day.</summary>
    public DateOnly? Max { get; set; }

    /// <summary>Days that cannot be selected.</summary>
    public IReadOnlyCollection<DateOnly>? Unavailable { get; set; }

    /// <summary>The fewest days a range may hold.</summary>
    public int? MinRange { get; set; }

    /// <summary>The most days a range may hold.</summary>
    public int? MaxRange { get; set; }

    /// <summary>The day the calendar opens to while nothing is chosen.</summary>
    public DateOnly? OpenTo { get; set; }

    /// <summary>Opens to <see cref="OpenTo" /> whatever is chosen.</summary>
    public bool? ForceOpenTo { get; set; }

    /// <summary>
    ///     What is picked: a day or a range. The step that opens the picker says it
    ///     (<c>Ui.DatePicker.Range.Bind(…)</c>); set again on an opened picker it has to agree with what is bound.
    /// </summary>
    public Ui.DatePickerMode? Mode { get; set; }

    /// <summary>How many months are shown side by side: one, or two for a range, unless set.</summary>
    public int? Months { get; set; }

    /// <summary>How large the calendar's day cells are.</summary>
    public Ui.DatePickerSize? Size { get; set; }

    /// <summary>The day the week starts on. The locale's own unless set.</summary>
    public DayOfWeek? StartDay { get; set; }

    /// <summary>Shows each week's number.</summary>
    public bool? WeekNumbers { get; set; }

    /// <summary>Makes the month and the year in the header selects.</summary>
    public bool? SelectableHeader { get; set; }

    /// <summary>Adds the shortcut that comes back to this month, and picks today once there.</summary>
    public bool? WithToday { get; set; }

    /// <summary>Always draws six weeks, so paging never changes the height.</summary>
    public bool? FixedWeeks { get; set; }

    /// <summary>Holds the choice until the reader confirms it with the popup's button.</summary>
    public bool? WithConfirmation { get; set; }

    /// <summary>Lists preset ranges beside the calendar.</summary>
    public bool? WithPresets { get; set; }

    /// <summary>Which presets are listed, in this order. Setting it lists them.</summary>
    public IReadOnlyList<Ui.DateRangePreset>? Presets { get; set; }

    /// <summary>
    ///     A button that empties the picker, shown while a date is chosen. A bound <c>DateOnly?</c> or
    ///     <c>UiDateRange?</c> becomes <see langword="null" />; one that cannot be goes back to its unset value.
    /// </summary>
    public bool? Clearable { get; set; }

    /// <summary>Prevents the reader from opening the picker.</summary>
    public bool? Disabled { get; set; }

    /// <inheritdoc cref="IUiFormControl.Invalid" />
    public bool? Invalid { get; set; }

    /// <summary>The locale the names, the week and the shown date follow. The app's culture unless set.</summary>
    public string? Locale { get; set; }

    /// <summary>What opens the picker instead of its own trigger: <c>Ui.DatePickerInput</c>s, a <c>Ui.DatePickerButton</c>.</summary>
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
    ///     Writes the model as soon as it is chosen, and validates then:
    ///     <c>Ui.DatePicker.Bind(() =&gt; m.From).Live()</c>. Flux's <c>wire:model.live</c>. Without it the choice is
    ///     written with the next action, and travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live { get; set; }

    /// <inheritdoc />
    DateOnly? IUiClock.Today { get; set; }

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    // The open state, the view month and a held choice are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private protected UiCalendarState State => _state;

    private protected string PopoverId => _popover;

    /// <summary>Two months for a range, as Flux defaults.</summary>
    private protected virtual int DefaultMonths => 1;

    /// <summary>The mode this picker's bound type is.</summary>
    private protected abstract Ui.DatePickerMode Bound { get; }

    private protected abstract string DefaultPlaceholder { get; }

    /// <summary>The popup's confirming button: "Select date", or "Select dates" for a range.</summary>
    private protected abstract string ConfirmLabel { get; }

    private bool Confirms => WithConfirmation == true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        UiDatePickerModes.Check(Mode, Bound);
        var (acc, ctx, bound) = UiFormCommit.Resolve<T>(this);
        var committed = Bind is null && _own.Held && EqualityComparer<T?>.Default.Equals(Value, _own.Given) ? _own.Value : bound;
        var shown = _pending.Held ? _pending.Value : committed;
        var options = Options();

        Task Commit(T value)
        {
            _own = (Bind is null, value, Value);
            _emptied = 0;
            return UiFormCommit.CommitAsync(this, acc, ctx, value);
        }

        Task Choose(T value) => Confirms ? Hold(value) : Commit(value);

        var scope = new UiDatePickerScope
        {
            PopoverId = PopoverId,
            CalendarId = PopoverId + "-calendar",
            ControlId = field.ControlId,
            Placeholder = Placeholder ?? DefaultPlaceholder,
            Culture = options.Culture,
            Text = Text(committed, options),
            Open = _open,
            Disabled = Disabled == true,
            Invalid = field.Invalid,
            Aria = field.Aria,
            Clearable = Clearable == true,
            Clear = () => ClearAsync(acc, ctx),
            Dates = Dates(committed),
            Typed = (slot, date) => Commit(Typed(committed, slot, date)),
            Untyped = (slot, half, fields) => UntypedAsync(slot, half, fields, acc, ctx, committed),
            FirstEdit = UiFormCommit.FirstEdit(acc, ctx),
            Show = () => _open = true,
        };

        var calendar = Calendar(scope.CalendarId, options, shown, Choose, Commit);

        return field.Wrap(
            // The popup hangs under the whole picker — under both fields of a range typed into two.
            Div.Class(UiClass.Compose(UiDatePickerLook.Root, Class))
                .Data(_open ? MarksOpen : Marks)
                .Attributes(("style", "anchor-name:--" + PopoverId))[
                Context.Provide(scope)[Trigger ?? OwnTrigger()],
                Popup(calendar)
            ]);
    }

    /// <inheritdoc />
    protected override Task OnRendered() => _state.RenameAsync(StateHasChanged);

    // A choice waiting for the popup's confirming button.
    private Task Hold(T value)
    {
        _pending = (true, value);
        return Task.CompletedTask;
    }

    // A typed field left with no date in it. Half filled is no date at all; emptied is nothing chosen — once
    // every field of the trigger is, since a range with one end is no range either.
    private Task UntypedAsync(
        int slot, bool half, int fields, ExpressionAccessor.Accessor? accessor, EditContext? context, T? committed)
    {
        var every = (1 << fields) - 1;
        _emptied = half ? _emptied & ~(1 << slot) : _emptied | (1 << slot);
        if (half || (_emptied & every) != every)
        {
            return UiFormCommit.UnreadAsync(this, accessor, context, committed, RaskString.ValidationDate);
        }

        return EmptyAsync(accessor, context);
    }

    // The clear button's: every typed field counts as emptied, and a choice still waiting to be confirmed goes
    // with the one that was written.
    private Task ClearAsync(ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        _emptied = ~0;
        _pending = default;
        return EmptyAsync(accessor, context);
    }

    // Nothing chosen, however the reader said so — the clear button, or every part of every field erased: null
    // where the bound member can say so, the type's unset value where it cannot.
    private Task EmptyAsync(ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        _own = (Bind is null, default, Value);
        return UiFormCommit.ClearAsync(this, accessor, context);
    }

    /// <summary>What "chosen" means for this picker, over the value as it stands.</summary>
    private protected abstract UiCalendarPicks Picks(T? current, Func<T, Task> choose);

    /// <summary>What the button shows for <paramref name="current" />; nothing while it is unset.</summary>
    private protected abstract string? Text(T? current, UiCalendarOptions options);

    /// <summary>The day each typed field holds: one, or a range's start and end.</summary>
    private protected abstract IReadOnlyList<DateOnly?> Dates(T? current);

    /// <summary>The value with <paramref name="date" /> typed into field <paramref name="slot" />.</summary>
    private protected abstract T Typed(T? current, int slot, DateOnly date);

    /// <summary>What stands before the months: a range picker's preset list.</summary>
    private protected virtual Component? Aside(T? current, UiCalendarOptions options, Func<T, Task> choose) => null;

    /// <summary>A day as the button shows it: the locale's long date without its weekday, the month abbreviated.</summary>
    private protected static string Medium(DateOnly date, CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.LongDatePattern
            .Replace("dddd, ", "", StringComparison.Ordinal)
            .Replace(", dddd", "", StringComparison.Ordinal)
            .Replace("dddd", "", StringComparison.Ordinal)
            .Replace("MMMM", "MMM", StringComparison.Ordinal)
            .Trim();

        return date.ToString(pattern, culture);
    }

    // The months in the popup, a range's presets before them and the confirming buttons under them.
    private Component Calendar(string id, UiCalendarOptions options, T? shown, Func<T, Task> choose, Func<T, Task> commit) =>
        UiCalendarGrid.Render(
            options with { Id = id, InPopup = true, Closes = Confirms ? null : PopoverId },
            _state,
            Picks(shown, choose),
            UiDatePickerLook.Calendar,
            Aside(shown, options, choose) ?? Div.Class(UiDatePickerLook.Aside),
            Footer(commit),
            marked: false);

    private Component OwnTrigger() =>
        Type == Ui.DatePickerType.Input ? Ui.DatePickerInput : Ui.DatePickerButton;

    private Component Popup(Component calendar) =>
        Dialog
            .Id(PopoverId)
            .Class(UiDatePickerLook.Dialog)
            .Popover(Rask.Core.Popover.Auto)
            // The runtime shows the popup when C# says so (an arrow key on the closed button), and holds the
            // page still behind it while it is open, as Flux does: no scroll, no pointer.
            .Data(_open ? PopupOpen : PopupClosed)
            // Flux focuses nothing when its picker opens. A <dialog popover> would hand the focus to its first
            // control (a month step, or the header's select with a ring); `autofocus` on the dialog itself keeps
            // it on the popup — no control lit, Tab enters the calendar, Escape returns to the trigger.
            .Attributes(
                ("autofocus", ""),
                (UiDraws.Mark, "toggle"),
                ("style", "position-anchor:--" + PopoverId
                          + ";inset:auto;top:anchor(bottom);left:anchor(left)"
                          + ";position-try-fallbacks:flip-block,flip-inline;margin:5px 0"))
            .OnToggle(e =>
            {
                _open = string.Equals(e.NewState, "open", StringComparison.Ordinal);
                if (!_open)
                {
                    // Reopening goes back to the chosen day's month, and a choice nobody confirmed is gone.
                    _state.Forget();
                    _pending = default;
                }
            })[
            Div.Class(UiDatePickerLook.FocusPlaceholder).Data(FocusPlaceholder.With(null)).TabIndex(0),
            calendar
        ];

    // Flux's two buttons, there in every picker and shown when a choice waits to be confirmed. The confirming
    // click is heard on the box around the button, where Flux hears it too: both close the popup themselves.
    private Component Footer(Func<T, Task> commit) =>
        Div.Class(Confirms ? UiDatePickerLook.Footer : UiDatePickerLook.FooterHidden)[
            Div.Class("inline")[
                Ui.Button.Ghost.CommandFor(PopoverId).Command("hide-popover")[RaskStrings.Get(RaskString.DatePickerCancel, "Cancel")]
            ],
            Div.Class("inline").Role("button").TabIndex(0)
                .Attributes(UiDraws.Marks(("click", UiDraws.Waits(this, Bind is not null))))
                .OnClick(() => _pending.Held ? commit(_pending.Value!) : Task.CompletedTask)[
                Ui.Button.Primary.CommandFor(PopoverId).Command("hide-popover")[ConfirmLabel]
            ]
        ];

    private UiCalendarOptions Options()
    {
        var culture = Locale is { } locale ? CultureInfo.GetCultureInfo(locale) : CultureInfo.CurrentCulture;

        return new UiCalendarOptions(
            ((IUiClock)this).Today ?? UiCalendarOptions.Now(),
            culture,
            StartDay ?? culture.DateTimeFormat.FirstDayOfWeek,
            UiDatePickerLook.Cells(Size),
            Math.Max(1, Months ?? DefaultMonths))
        {
            Min = Min,
            Max = Max,
            Unavailable = Unavailable,
            MinRange = MinRange,
            MaxRange = MaxRange,
            OpenTo = OpenTo,
            ForceOpenTo = ForceOpenTo == true,
            WeekNumbers = WeekNumbers == true,
            SelectableHeader = SelectableHeader == true,
            WithToday = WithToday == true,
            FixedWeeks = FixedWeeks == true,
            PickDraws = UiDraws.Waits(this, Bind is not null),
        };
    }
}
