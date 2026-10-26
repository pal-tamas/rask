using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's time picker: <c>Ui.TimePicker.Bind(() =&gt; m.StartsAt).Label("Starts at")</c>.
/// </summary>
/// <remarks>
/// <para>
/// A form control over a time of day. <typeparamref name="T" /> is what it is bound to and says how many it
/// picks: a <c>TimeOnly?</c> is one time or none, a <c>TimeOnly</c> always one, and a collection of
/// <c>TimeOnly</c> (<c>List&lt;TimeOnly&gt;</c>, <c>TimeOnly[]</c>, <c>HashSet&lt;TimeOnly&gt;</c>) is Flux's
/// <c>multiple</c>. <c>.Bind(() =&gt; model.At)</c> two-way binds and drives the surrounding form's
/// validation; <c>.Value(x)</c> with <c>OnChange</c> leaves the value with the parent;
/// <c>.Of&lt;TimeOnly?&gt;()</c> opens one with no value yet.
/// </para>
/// <para>
/// The trigger is a button showing the choice, or — <see cref="Ui.TimePickerType.Input" /> — hour, minute and
/// AM/PM fields to type into. The list is a <c>popover</c> the browser owns: the top layer, Escape, a click
/// outside. The button is the <c>combobox</c> and keeps the focus; the arrows move a cursor through the list
/// (<c>aria-activedescendant</c>) and Enter picks. The cursor, the row under the pointer and the list opening
/// are the browser's (<c>rask-time-list.ts</c>): only a pick reaches the page.
/// </para>
/// <para>
/// <c>Label</c>, <c>Description</c>, <c>DescriptionTrailing</c> and <c>Badge</c> wrap it in a
/// <see cref="UiField" /> with its <see cref="UiError" />, as Flux's shorthand does.
/// </para>
/// </remarks>
public sealed partial class UiTimePicker<T> : Component, IFormControl<T>, IUiFormControl, IDeferredBind
{
    private readonly int _instance = UiTimePickerLook.NextInstance();
    private string? _ownId;

    // The binding as the last render resolved it, inside its form: a handler reads the value through it as it
    // is when the handler runs.
    private ExpressionAccessor.Accessor? _accessor;
    private EditContext? _context;

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <inheritdoc cref="IUiFormControl.Badge" />
    public string? Badge { get; set; }

    /// <summary>What the button shows while no time is chosen. "Select a time" unless this says otherwise.</summary>
    public string? Placeholder { get; set; }

    /// <summary>A button, or fields to type the time into.</summary>
    public Ui.TimePickerType? Type { get; set; }

    /// <summary>12-hour or 24-hour. The culture's own unless set.</summary>
    public Ui.TimePickerTimeFormat? TimeFormat { get; set; }

    /// <summary>Minutes between the times on offer. 30 unless set.</summary>
    public int? Interval { get; set; }

    /// <summary>The earliest time on offer.</summary>
    public TimeOnly? Min { get; set; }

    /// <summary>The latest time on offer.</summary>
    public TimeOnly? Max { get; set; }

    /// <summary>Times, and stretches of time, that are listed and cannot be picked.</summary>
    public IReadOnlyList<UiTimeRange>? Unavailable { get; set; }

    /// <summary>Where the list opens while nothing is chosen.</summary>
    public TimeOnly? OpenTo { get; set; }

    /// <summary>How tall it is. 40px unless this says smaller.</summary>
    public Ui.TimePickerSize? Size { get; set; }

    /// <summary>A button that empties the picker, shown while a time is chosen.</summary>
    public bool? Clearable { get; set; }

    /// <summary>Takes no input and is skipped by the keyboard.</summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     Draws the error state. A bound picker is invalid on its own while its form holds a message for it.
    /// </summary>
    public bool? Invalid { get; set; }

    /// <summary>The culture the times are written in — <c>"ja-JP"</c>. The app's current culture unless set.</summary>
    public string? Locale { get; set; }

    /// <summary>False leaves the typed trigger without its list.</summary>
    public bool? Dropdown { get; set; }

    /// <summary>Classes for the picker's root: widths and margins.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="Element.Id" />
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
    ///     Writes the model as soon as it is chosen, and validates then:
    ///     <c>Ui.TimePicker.Bind(() =&gt; m.At).Live()</c>. Flux's <c>wire:model.live</c>. Without it the choice is
    ///     written with the next action, and travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live { get; set; }

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    // A pick the session holds for the next action is drawn from there, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string ListId => "uitp-" + _instance.ToString(CultureInfo.InvariantCulture);

    private CultureInfo Culture => Locale is { } name ? CultureInfo.GetCultureInfo(name) : CultureInfo.CurrentCulture;

    private Ui.TimePickerTimeFormat Format => TimeFormat ?? Ui.TimePickerTimeFormat.Auto;

    private bool Typed => Type == Ui.TimePickerType.Input && !UiTimePickerValue.Several<T>();

    // Whether a picked time is held for the next action. Several times are written as they are picked.
    private bool PickDraws => !UiTimePickerValue.Several<T>() && UiDraws.Waits(this, Bind is not null);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        (_accessor, _context) = (accessor, context);
        var chosen = UiTimePickerValue.Read(current);
        var times = UiTimePickerLook.Times(Min, Max, Interval ?? 30);
        var clears = Clearable == true && Disabled != true && chosen.Count > 0 && UiTimePickerValue.Clears<T>();

        return field.Wrap(
            Div.Class(UiClass.Compose(
                    clears ? UiTimePickerLook.RootClearable : UiTimePickerLook.Root,
                    UiPickerWidth.For(Class),
                    UiTimePickerValue.Several<T>() ? UiTimePickerLook.RootSeveral : null,
                    Class))
                .Data(UiTimePickerLook.RootMarks)[
                // Keyed, so the clear button coming and going is an insert or a removal the live diff ships — and
                // written after the list it is laid over, so the times keep the handlers they had.
                Div.Key("trigger").Class("inline")[Typed ? TypedTrigger(field, accessor, context, chosen) : ButtonTrigger(field, chosen)],
                Options(times, chosen),
                clears ? UiPickerClear.Render(UiTimePickerLook.Height(Size ?? Ui.TimePickerSize.Base), ClearAsync) : null
            ]);
    }

    private Component ButtonTrigger(UiWithField field, List<TimeOnly> chosen)
    {
        // Rendered as it is while the list is shut: the runtime writes it open, and the row the cursor is on, as
        // the browser opens the list and the keys move.
        var aria = new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal)
        {
            ["controls"] = ListId,
            ["haspopup"] = "listbox",
            ["expanded"] = "false",
        };

        return Button
            .Id(field.ControlId)
            .Type(ButtonType.Button)
            .Role("combobox")
            .Class(UiClass.Compose(UiTimePickerLook.Button, UiTimePickerLook.ButtonSize(Size ?? Ui.TimePickerSize.Base)))
            .Disabled(Disabled == true)
            .Data(field.Invalid ? UiTimePickerLook.InvalidButtonMarks : UiTimePickerLook.ButtonMarks)
            .Aria(aria)
            // The browser opens and closes the list from the button. Closed, Enter does not press it and the
            // arrows do not scroll the page: Flux's button, not a native one.
            .Attributes(("popovertarget", ListId), ("data-rask-listbox-button", null), ("data-rask-open-mark", ListId), ("style", "anchor-name:--" + ListId))[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.ButtonIcon),
            Div.Class(UiTimePickerLook.Selected)[
                chosen.Count == 0
                    // One or the other, each under its key: a span against a div is no change the live diff can patch.
                    ? Span.Key("placeholder").Class(UiTimePickerLook.Placeholder).Data("ui-time-picker-placeholder", null)[Placeholder ?? RaskStrings.Get(RaskString.TimePickerPlaceholder, "Select a time")]
                    : Div.Key("chosen").Class("overflow-hidden").Attributes(("dir", "auto"))[string.Join(", ", chosen.Select(Written))]
            ],
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.ButtonChevron)
        ];
    }

    private Component Options(List<TimeOnly> times, List<TimeOnly> chosen)
    {
        // A typed trigger with its list taken away keeps the element, as Flux does, and nothing can open it.
        var listed = !Typed || Dropdown != false;
        var start = Start(times, chosen);
        var list = Div
            .Key("list")
            .Id(ListId)
            .Popover(Popover.Auto)
            .TabIndex(-1)
            .Class(UiTimePickerLook.Options)
            // While it is open the page behind it neither scrolls nor takes the pointer, as on Flux.
            .Data(UiTimePickerLook.ListMarks)
            .Attributes(
                ("style", "position-anchor:--" + ListId
                          + ";inset:auto;top:calc(anchor(bottom) + 5px);inset-inline-start:anchor(start)"
                          + ";width:anchor-size(width);position-try-fallbacks:flip-block"));

        if (listed)
        {
            list = list.Role("listbox").Aria("multiselectable", UiTimePickerValue.Several<T>() ? "true" : "false");
        }

        return list[times.Select((time, index) => Option(time, index, chosen.Contains(time), index == start))];
    }

    private Component Option(TimeOnly time, int index, bool selected, bool start)
    {
        var off = Off(time);
        var option = UiTimeParts.Row(ListId, index, time, selected, start, off);

        if (!off)
        {
            option = option.OnClick(() => PickAsync(time));

            // A picked time closes the list on the same click; several stay open for the next.
            option = UiTimePickerValue.Several<T>()
                ? option
                : option.Attributes([("popovertarget", ListId), ("popovertargetaction", "hide"), .. UiDraws.Pairs(("click", PickDraws))]);
        }

        return option[UiTimeParts.RowContent(Written(time), selected)];
    }

    private string Written(TimeOnly time) => UiTimePickerLook.Format(time, Format, Culture);

    private bool Off(TimeOnly time) => Unavailable?.Any(stretch => stretch.Contains(time)) == true;

    // Where the list opens: on the chosen time, else on OpenTo, else on the first time that can be picked.
    private int Start(List<TimeOnly> times, List<TimeOnly> chosen)
    {
        bool Pickable(int index) => index >= 0 && index < times.Count && !Off(times[index]);
        var from = chosen.Count > 0 ? chosen[0] : OpenTo;
        var seed = from is { } at ? times.FindIndex(time => time >= at) : -1;
        return Pickable(seed) ? seed : UiSelectNav.FirstEnabled(times.Count, index => !Pickable(index));
    }

    // The binding and the times chosen as they are when a handler runs, not as they were drawn.
    private Bound Latest() => _accessor is { } accessor && Bind is not null
        ? new Bound(accessor, _context, UiTimePickerValue.Read(UiFormCommit.Shown(this, accessor)))
        : new Bound(null, null, UiTimePickerValue.Read(Value));

    private Task ClearAsync() => CommitAsync(Latest(), []);

    // A press on a time: several toggle it, one replaces the choice — and a second press on the chosen time
    // takes it back where the value can be empty.
    private Task PickAsync(TimeOnly time)
    {
        var bound = Latest();
        if (UiTimePickerValue.Several<T>())
        {
            List<TimeOnly> picked = bound.Chosen.Contains(time) ? [.. bound.Chosen.Where(chosen => chosen != time)] : [.. bound.Chosen, time];
            picked.Sort();
            return CommitAsync(bound, picked);
        }

        return bound.Chosen.Contains(time) && UiTimePickerValue.Clears<T>() ? CommitAsync(bound, []) : CommitAsync(bound, [time]);
    }

    private async Task CommitAsync(Bound bound, List<TimeOnly> picked)
    {
        if (!UiTimePickerValue.Several<T>())
        {
            var one = UiTimePickerValue.Single<T>(picked.Count > 0 ? picked[0] : null);
            await UiFormCommit.CommitAsync(this, bound.Accessor, bound.Context, one).ConfigureAwait(false);
            return;
        }

        var self = (IFormControl<T>)this;
        ReaderEdits.Note(this);
        if (bound.Accessor is not { } accessor)
        {
            await self.InvokeOnChange((T)UiTimePickerValue.Shaped(typeof(T), picked)).ConfigureAwait(false);
            return;
        }

        if (UiTimePickerValue.TryWrite(accessor, picked))
        {
            await BindingHelpers.NotifyAndValidateField(bound.Context, accessor.Field).ConfigureAwait(false);
            await self.InvokeAfterBind((T)accessor.Getter()!).ConfigureAwait(false);
        }
    }

    private sealed record Bound(ExpressionAccessor.Accessor? Accessor, EditContext? Context, List<TimeOnly> Chosen);
}
