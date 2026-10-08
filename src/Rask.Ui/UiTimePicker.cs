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
/// (<c>aria-activedescendant</c>) and Enter picks.
/// </para>
/// <para>
/// <c>Label</c>, <c>Description</c>, <c>DescriptionTrailing</c> and <c>Badge</c> wrap it in a
/// <see cref="UiField" /> with its <see cref="UiError" />, as Flux's shorthand does.
/// </para>
/// </remarks>
public sealed partial class UiTimePicker<T> : Component, IFormControl<T>, IUiFormControl
{
    private readonly int _instance = UiTimePickerLook.NextInstance();
    private bool _open;
    private int _cursor = -1;

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

    string IUiFieldControl.ControlId => UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    // Whether the list is open and where its cursor is are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string ListId => "uitp-" + _instance.ToString(CultureInfo.InvariantCulture);

    private CultureInfo Culture => Locale is { } name ? CultureInfo.GetCultureInfo(name) : CultureInfo.CurrentCulture;

    private Ui.TimePickerTimeFormat Format => TimeFormat ?? Ui.TimePickerTimeFormat.Auto;

    private bool Typed => Type == Ui.TimePickerType.Input && !UiTimePickerValue.Several<T>();

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        var times = UiTimePickerLook.Times(Min, Max, Interval ?? 30);
        var view = new View(accessor, context, times, UiTimePickerValue.Read(current), field);
        var cursor = Cursor(view);
        var clears = Clearable == true && Disabled != true && view.Chosen.Count > 0 && UiTimePickerValue.Clears<T>();

        return field.Wrap(
            Div.Class(UiClass.Compose(clears ? UiTimePickerLook.RootClearable : UiTimePickerLook.Root, Class))
                .Data(UiTimePickerLook.RootMarks)[
                Div.Class("inline")[Typed ? TypedTrigger(view) : ButtonTrigger(view, cursor)],
                clears ? ClearButton(view) : null,
                Options(view, cursor)
            ]);
    }

    private Component ButtonTrigger(View view, int cursor)
    {
        var aria = new Dictionary<string, string?>(view.Field.Aria, StringComparer.Ordinal)
        {
            ["controls"] = ListId,
            ["haspopup"] = "listbox",
            ["expanded"] = _open ? "true" : "false",
        };
        if (_open && cursor >= 0)
        {
            aria["activedescendant"] = UiSelectNav.OptId(ListId, cursor);
        }

        return Button
            .Id(view.Field.ControlId)
            .Type(ButtonType.Button)
            .Role("combobox")
            .Class(UiClass.Compose(UiTimePickerLook.Button, UiTimePickerLook.ButtonSize(Size ?? Ui.TimePickerSize.Base)))
            .Disabled(Disabled == true)
            .Data(view.Field.Invalid ? UiTimePickerLook.InvalidButtonMarks : UiTimePickerLook.ButtonMarks)
            .Aria(aria)
            // The browser opens and closes the list from the button; its toggle event below says which.
            .Attributes(("popovertarget", ListId), ("style", "anchor-name:--" + ListId))
            .OnKeyDown(e => OnKeyAsync(e, view))[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.ButtonIcon),
            Div.Class(UiTimePickerLook.Selected)[
                view.Chosen.Count == 0
                    ? Span.Class(UiTimePickerLook.Placeholder).Data("ui-time-picker-placeholder", null)[Placeholder ?? "Select a time"]
                    : Div.Attributes(("dir", "auto"))[string.Join(", ", view.Chosen.Select(Written))]
            ],
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.ButtonChevron)
        ];
    }

    private Component Options(View view, int cursor)
    {
        // A typed trigger with its list taken away keeps the element, as Flux does, and nothing can open it.
        var listed = !Typed || Dropdown != false;
        var list = Div
            .Id(ListId)
            .Popover(Popover.Auto)
            .TabIndex(-1)
            .Class(UiTimePickerLook.Options)
            // The runtime shows or hides the list when C# changes its mind: ArrowDown on a closed picker, Enter
            // on an open one, a press on the typed trigger.
            .Data("rask-popover-open", _open ? "true" : "false")
            .Attributes(("style", "position-anchor:--" + ListId
                                  + ";inset:auto;top:calc(anchor(bottom) + 5px);inset-inline-start:anchor(start)"
                                  + ";width:anchor-size(width);position-try-fallbacks:flip-block"))
            .OnToggle(e =>
            {
                _open = string.Equals(e.NewState, "open", StringComparison.Ordinal);
                _cursor = -1;
            });

        if (listed)
        {
            list = list.Role("listbox").Aria("multiselectable", UiTimePickerValue.Several<T>() ? "true" : "false");
        }

        return list[view.Times.Select((time, index) => Option(view, time, index, index == cursor))];
    }

    private Component Option(View view, TimeOnly time, int index, bool active)
    {
        var off = Off(time);
        var selected = view.Chosen.Contains(time);
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["time"] = time.ToString("HH:mm", CultureInfo.InvariantCulture),
        };
        if (selected)
        {
            data["selected"] = null;
        }

        if (active)
        {
            data["active"] = null;
        }

        var option = Button
            .Key(index)
            .Id(UiSelectNav.OptId(ListId, index))
            .Type(ButtonType.Button)
            .Role("option")
            .TabIndex(-1)
            .Class(UiTimePickerLook.Option)
            .Disabled(off)
            .Data(data)
            .Aria("selected", selected ? "true" : "false");

        if (!off)
        {
            option = option
                .OnClick(() => PickAsync(view, time))
                // One highlight for the pointer and the keyboard, as Flux has: the cursor goes where the pointer is.
                .OnMouseEnter(() => _cursor = index);
            if (!UiTimePickerValue.Several<T>())
            {
                // A picked time closes the list on the same click; several stay open for the next.
                option = option.Attributes(("popovertarget", ListId), ("popovertargetaction", "hide"));
            }
        }

        return option[
            Div.Class(UiTimePickerLook.Check).Data("checked", null)[
                Ui.Icon.Name(Ui.IconName.Check).Mini.Class(selected ? null : "hidden")
            ],
            Div.Attributes(("dir", "ltr"))[Written(time)]
        ];
    }

    private Component ClearButton(View view) =>
        Button
            .Type(ButtonType.Button)
            .Class(UiTimePickerLook.Clear)
            .Aria("label", "Clear")
            .Data("ui-button", null)
            .OnClick(() => CommitAsync(view, []))[
            Ui.Icon.Name(Ui.IconName.XMark).Mini
        ];

    private string Written(TimeOnly time) => UiTimePickerLook.Format(time, Format, Culture);

    private bool Off(TimeOnly time) => Unavailable?.Any(stretch => stretch.Contains(time)) == true;

    // Where the cursor is: where the keyboard or the pointer left it, else on the chosen time, else on
    // OpenTo, else on the first time that can be picked.
    private int Cursor(View view)
    {
        bool Pickable(int index) => index >= 0 && index < view.Times.Count && !Off(view.Times[index]);
        if (Pickable(_cursor))
        {
            return _cursor;
        }

        var start = view.Chosen.Count > 0 ? view.Chosen[0] : OpenTo;
        var seed = start is { } from ? view.Times.FindIndex(time => time >= from) : -1;
        return Pickable(seed) ? seed : UiSelectNav.FirstEnabled(view.Times.Count, index => !Pickable(index));
    }

    // The combobox's keys. The runtime keeps them from scrolling the page while the list is open.
    private async Task OnKeyAsync(KeyboardEvent e, View view)
    {
        bool Disabled(int index) => Off(view.Times[index]);
        var count = view.Times.Count;
        var cursor = Cursor(view);

        if (!_open)
        {
            // Closed, an arrow opens the list where it would have opened by a press.
            _open = e.Key is Keys.ArrowDown or Keys.ArrowUp;
            return;
        }

        switch (e.Key)
        {
            case Keys.ArrowDown:
                _cursor = UiSelectNav.Step(cursor, 1, count, Disabled);
                break;
            case Keys.ArrowUp:
                _cursor = UiSelectNav.Step(cursor, -1, count, Disabled);
                break;
            case Keys.Home:
                _cursor = UiSelectNav.FirstEnabled(count, Disabled);
                break;
            case Keys.End:
                _cursor = UiSelectNav.LastEnabled(count, Disabled);
                break;
            case Keys.Enter when cursor >= 0:
                _open = UiTimePickerValue.Several<T>();
                await PickAsync(view, view.Times[cursor]).ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    // A press on a time: several toggle it, one replaces the choice — and a second press on the chosen time
    // takes it back where the value can be empty.
    private Task PickAsync(View view, TimeOnly time)
    {
        if (UiTimePickerValue.Several<T>())
        {
            List<TimeOnly> picked = view.Chosen.Contains(time) ? [.. view.Chosen.Where(chosen => chosen != time)] : [.. view.Chosen, time];
            picked.Sort();
            return CommitAsync(view, picked);
        }

        return view.Chosen.Contains(time) && UiTimePickerValue.Clears<T>() ? CommitAsync(view, []) : CommitAsync(view, [time]);
    }

    private async Task CommitAsync(View view, List<TimeOnly> picked)
    {
        if (!UiTimePickerValue.Several<T>())
        {
            var one = UiTimePickerValue.Single<T>(picked.Count > 0 ? picked[0] : null);
            await UiFormCommit.CommitAsync(this, view.Accessor, view.Context, one).ConfigureAwait(false);
            return;
        }

        var self = (IFormControl<T>)this;
        if (view.Accessor is not { } accessor)
        {
            await self.InvokeOnChange((T)UiTimePickerValue.Shaped(typeof(T), picked)).ConfigureAwait(false);
            return;
        }

        if (UiTimePickerValue.TryWrite(accessor, picked))
        {
            await BindingHelpers.NotifyAndValidateField(view.Context, accessor.Field).ConfigureAwait(false);
            await self.InvokeAfterBind((T)accessor.Getter()!).ConfigureAwait(false);
        }
    }

    private sealed record View(
        ExpressionAccessor.Accessor? Accessor,
        EditContext? Context,
        List<TimeOnly> Times,
        List<TimeOnly> Chosen,
        UiWithField Field);
}
