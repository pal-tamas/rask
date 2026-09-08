using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Ui;

/// <summary>
/// A field with a fixed set of answers.
/// </summary>
/// <remarks>
/// <para>
/// A real <c>&lt;select&gt;</c> by default, and that is the version to reach for: it works with a
/// keyboard, a screen reader and a phone's native picker without a line of script, and it renders
/// complete on a prerendered page. <see cref="Native" /> set to <c>false</c> draws the list instead —
/// see the property for what that buys and what it costs.
/// </para>
/// <para>
/// Both modes are the same control. <see cref="Options" />, <see cref="Value" />, <see cref="Bind" />,
/// <see cref="OnChange" />, <see cref="Placeholder" />, <see cref="Tone" />, <see cref="Size" /> and
/// <see cref="Disabled" /> mean exactly the same thing either way; the flag chooses how the list is
/// DRAWN, not what the control is.
/// </para>
/// <para>
/// It is a form control: <c>.Bind(() =&gt; model.Country)</c> two-way binds and drives the surrounding
/// <c>Form</c>'s validation, or <see cref="Value" /> with <see cref="OnChange" /> lets the parent own
/// the value. The two are mutually exclusive at the call site — the generator emits a factory for each
/// and excludes the other mode's members from it.
/// </para>
/// </remarks>
public sealed partial class UiSelect<T> : Component, IFormControl<T>
{
    // Per-instance, so two id-less selects on one page cannot collide on option ids —
    // aria-activedescendant points at them by id, and a collision aims it at the wrong list.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    private bool _open;
    private int _cursor = -1;

    /// <summary>The accessible name.</summary>
    public required string Label { get; set; }

    /// <summary>
    ///     The options: the value stored, and the words shown.
    /// </summary>
    /// <remarks>
    ///     Not the step that pins <typeparamref name="T" /> — the chain's OPENING does that, and for a
    ///     form control the opening is <see cref="Value" /> or <see cref="Bind" />, which fix the type
    ///     and the mode together. So a call site reads
    ///     <c>UiSelect.Value(x).Options(…).Label(…)</c>, and <c>Label</c>/<c>Options</c> may come in
    ///     either order after it.
    /// </remarks>
    public required IReadOnlyList<(T Value, string Text)> Options { get; set; }

    /// <summary>Shown first and unselectable — the prompt, not an answer.</summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     Draw the list here instead of handing it to the platform. Unset is the real
    ///     <c>&lt;select&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Turn it off when the option list has to carry more than the platform will show — a
    ///         grouped list, options that are visibly unavailable, a list that must escape an
    ///         <c>overflow: hidden</c> ancestor. The list is a <c>[popover]</c>, so the browser gives it
    ///         the top layer and dismisses it on Escape and on a click outside.
    ///     </para>
    ///     <para>
    ///         <b>It needs the runtime.</b> The custom list is inert on a prerendered page and does
    ///         nothing with scripting off, where the native control is completely working. That is the
    ///         cost of leaving the platform's own control, and it is why this defaults to native.
    ///     </para>
    ///     <para>
    ///         Its placement uses CSS anchor positioning, which not every engine ships yet; where it is
    ///         missing the list still opens and is still usable, centred rather than under its box. The
    ///         same trade <see cref="UiMegamenu" /> already makes.
    ///     </para>
    /// </remarks>
    public bool? Native { get; set; }

    /// <summary>
    ///     Marks options unselectable. The keyboard cursor skips them rather than landing on one.
    /// </summary>
    /// <remarks>Non-native only — a native <c>&lt;select&gt;</c> disables its options itself.</remarks>
    public Func<T, bool>? OptionDisabled { get; set; }

    /// <summary>Buckets options under headers, in first-seen order.</summary>
    /// <remarks>
    ///     Grouping reorders the flat option list rather than nesting it, so the arrow keys still move
    ///     to the next option a reader can SEE.
    /// </remarks>
    public Func<T, string>? OptionGroup { get; set; }

    /// <summary>
    ///     Posts the value from a plain HTML form.
    /// </summary>
    /// <remarks>
    ///     Non-native only, and it renders a hidden input: a listbox built from buttons submits nothing
    ///     on its own, so without this a control inside a <c>&lt;form&gt;</c> would silently drop its
    ///     field. The native control posts under its own name and ignores this.
    /// </remarks>
    public string? Name { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    public T? Value { get; set; }

    /// <inheritdoc />
    public Action<T>? OnChange { get; set; }

    /// <inheritdoc />
    public Func<T, Task>? OnChangeAsync { get; set; }

    /// <inheritdoc />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<T>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<T>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Action<T>? AfterBind { get; set; }

    /// <inheritdoc />
    public Func<T, Task>? AfterBindAsync { get; set; }

    private string Prefix => "uisel-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string ListId => Prefix + "-list";

    /// <inheritdoc />
    protected override Component? Render() => Native == false ? Custom() : NativeSelect();

    // The platform's control. Everything form-shaped is forwarded to Rask.Html's Select<T>, which is
    // itself an IFormControl<T> — so binding, validation registration and the change parse are the
    // framework's here rather than reimplemented, and there is exactly one place they can drift from.
    private Component NativeSelect()
    {
        // Bound and controlled are different CHAIN TYPES, not two settings on one — Bind and Value are
        // mutually exclusive openings, so each mode is built as its own complete expression rather than
        // by mutating a shared variable.
        var current = Current();
        var options = OptionRows(current);

        if (Bind is { } bind)
        {
            return Select
                .Bind(bind)
                .Validate(Validate)
                .ValidateAsync(ValidateAsync)
                .AfterBind(AfterBind)
                .AfterBindAsync(AfterBindAsync)
                .Aria(Aria(expanded: null))
                .Disabled(Disabled == true)
                .Class(BoxClass())[options];
        }

        return Select
            .Value(Value)
            .OnChange(OnChange)
            .OnChangeAsync(OnChangeAsync)
            .Aria(Aria(expanded: null))
            .Disabled(Disabled == true)
            .Class(BoxClass())[options];
    }

    private IEnumerable<Component?> OptionRows(T? current)
    {
        // `disabled` as well as empty: a placeholder that can be chosen is an answer, and one chosen by
        // accident is a bug report about a form that saved nothing.
        if (Placeholder is { } placeholder)
        {
            yield return Option.Value(string.Empty).Disabled(true).Selected(current is null)[placeholder];
        }

        foreach (var (value, text) in Options)
        {
            yield return Option
                .Key(OptionText(value))
                .Value(OptionText(value))
                .Selected(Same(value, current))[text];
        }
    }

    // The drawn list. A <button role="combobox"> naming a [popover] <ul role="listbox"> — the browser
    // owns dismissal, C# owns the state, and the two are kept in step by the popover's toggle event.
    private Component Custom()
    {
        var acc = Bind is { } bind ? ExpressionAccessor.Parse(bind) : null;
        var ctx = acc is null ? null : BindingHelpers.ResolveBindingContext(acc.Target);
        if (acc is not null)
        {
            // Every render, deliberately: passing the collapsed validator each time is also what clears
            // a stale rule when the consumer stops supplying one.
            ((IFormControl<T>)this).RegisterValidator(acc, ctx);
        }

        var current = acc is not null ? acc.Getter() is T v ? v : default : Value;
        var layout = UiSelectNav.Build(Options, OptionGroup is { } g ? o => g(o.Value) : null);
        var flat = layout.Flat;
        var disabled = Disabledness(flat);
        var cursor = UiSelectNav.Normalize(_cursor, flat.Count, disabled);

        var box = Button
            .Type("button")
            .Role("combobox")
            .Class(UiClass.Compose(BoxClass(), "flex items-center justify-between text-left"))
            .Disabled(Disabled == true)
            .Aria(Aria(expanded: _open, activeDescendant: _open && cursor >= 0
                ? UiSelectNav.OptId(Prefix, cursor)
                : null))
            .Attributes(
                ("popovertarget", ListId),
                // The anchor the list positions against. A custom property in a style attribute, not a
                // class — nothing here is scanned by Tailwind, so building the name is safe.
                ("style", "anchor-name:--" + Prefix))
            .OnClick(() =>
            {
                // Mirrors what the browser is about to do. Enter and Space reach this too, through the
                // button's own activation, which is how the keyboard opens the list.
                _open = !_open;
                _cursor = _open ? UiSelectNav.Seed(IndexOf(flat, current), flat.Count, disabled) : -1;
            })
            .OnKeyDownAsync(e => OnKeyAsync(e, acc, ctx, flat, disabled, current));

        var list = Ul
            .Id(ListId)
            .Role("listbox")
            .Popover("auto")
            .Class("dropdown-content menu z-1 max-h-64 w-full flex-nowrap overflow-y-auto rounded-box "
                + "bg-base-100 p-2 shadow-sm")
            .Attributes(("style", "position-anchor:--" + Prefix))
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            // The half that only the browser knows: it closes itself on Escape and on a click outside,
            // and without hearing that, aria-expanded above would go on claiming the list is open.
            .OnToggle(e =>
            {
                _open = e.IsOpen;
                if (!_open)
                {
                    _cursor = -1;
                }
            })[
            Rows(layout, acc, ctx, current, cursor)
        ];

        return Div.Class(UiClass.Compose("dropdown w-full", Class))[
            box[Span.Class("truncate")[Display(current)]],
            list,
            // A listbox of buttons submits nothing. Without this a control inside a plain <form> would
            // silently drop its field, which is the kind of failure nobody sees until the data is wrong.
            Name is { } name
                ? Input.Value(current is null ? string.Empty : OptionText(current)).Type(InputType.Hidden).Name(name)
                : null
        ];
    }

    private IEnumerable<Component?> Rows(
        UiSelectNav.Layout<(T Value, string Text)> layout,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        T? current,
        int cursor)
    {
        foreach (var group in layout.Groups)
        {
            if (group.Header is { } header)
            {
                yield return Li.Key("g-" + header).Class("menu-title")[header];
            }

            foreach (var row in group.Rows)
            {
                yield return Row(row, acc, ctx, current, cursor);
            }
        }
    }

    private Component Row(
        UiSelectNav.FlatRow<(T Value, string Text)> row,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        T? current,
        int cursor)
    {
        var (value, text) = row.Item;
        var off = OptionDisabled?.Invoke(value) == true;
        var selected = Same(value, current);

        var option = Button
            .Type("button")
            .Role("option")
            .Id(UiSelectNav.OptId(Prefix, row.FlatIndex))
            .Class(UiClass.Compose(
                // menu-active is the SELECTED option; menu-focus is where the keyboard cursor sits.
                // They are different things and daisyUI draws them differently — the cursor can be on an
                // option that is not the selected one, which is the whole point of a roving cursor.
                selected ? "menu-active" : "",
                cursor == row.FlatIndex ? "menu-focus" : ""))
            .Disabled(off)
            // aria-disabled is OMITTED when the option is enabled, never nulled. A null renders the
            // attribute valueless, and a valueless aria-disabled reads as "true" — so the tidy
            // conditional value would have made every selectable option announce itself unavailable.
            .Aria(off
                ? new Dictionary<string, string?> { ["selected"] = "false", ["disabled"] = "true" }
                : new Dictionary<string, string?> { ["selected"] = selected ? "true" : "false" })
            // Closes the list declaratively as well as through the callback, so the dismissal does not
            // depend on the runtime having attached anything.
            .Attributes(("popovertarget", ListId), ("popovertargetaction", "hide"));

        if (!off)
        {
            option = option.OnClickAsync(() => CommitAsync(acc, ctx, value));
        }

        // menu-disabled goes on the <li>, unlike menu-active and menu-focus, which go on the child.
        return Li.Key(row.FlatIndex).Class(off ? "menu-disabled" : "")[option[text]];
    }

    // Combobox keyboard over the flat option list: arrows move the cursor (skipping disabled options),
    // Home/End jump to the first/last enabled one, Enter picks. Escape is left alone — the browser's
    // own dismissal is what closes the popover, and OnToggle above hears about it.
    private async Task OnKeyAsync(
        KeyboardEventArgs e,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        IReadOnlyList<(T Value, string Text)> flat,
        Func<int, bool> disabled,
        T? current)
    {
        var count = flat.Count;
        var cursor = UiSelectNav.Normalize(_cursor, count, disabled);

        if (!_open)
        {
            // Closed, the arrows move the SELECTION rather than opening the list — what a native select
            // does on a desktop, and what lets a reader change the answer without ever seeing the list.
            switch (e.Key)
            {
                case "ArrowDown":
                case "ArrowUp":
                    var next = UiSelectNav.Step(
                        IndexOf(flat, current), e.Key == "ArrowDown" ? 1 : -1, count, disabled);
                    if (next >= 0 && next < count)
                    {
                        await CommitAsync(acc, ctx, flat[next].Value).ConfigureAwait(false);
                    }

                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case "ArrowDown":
                _cursor = UiSelectNav.Step(cursor, 1, count, disabled);
                break;
            case "ArrowUp":
                _cursor = UiSelectNav.Step(cursor, -1, count, disabled);
                break;
            case "Home":
                _cursor = UiSelectNav.FirstEnabled(count, disabled);
                break;
            case "End":
                _cursor = UiSelectNav.LastEnabled(count, disabled);
                break;
            case "Enter":
                if (cursor >= 0 && cursor < count && !disabled(cursor))
                {
                    await CommitAsync(acc, ctx, flat[cursor].Value).ConfigureAwait(false);
                }

                break;
        }
    }

    // Writes the chosen value back to the model (bound) or notifies the parent (controlled), then
    // closes. No StateHasChanged: Rask re-renders the callback's owner already (RASK026).
    private async Task CommitAsync(ExpressionAccessor.Accessor? acc, EditContext? ctx, T value)
    {
        _open = false;
        _cursor = -1;

        var self = (IFormControl<T>)this;
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, acc.Field).ConfigureAwait(false);
            await self.InvokeAfterBindAsync(value).ConfigureAwait(false);
        }
        else
        {
            await self.InvokeOnChangeAsync(value).ConfigureAwait(false);
        }
    }

    private Func<int, bool> Disabledness(IReadOnlyList<(T Value, string Text)> flat) =>
        OptionDisabled is { } off ? i => off(flat[i].Value) : _ => false;

    private int IndexOf(IReadOnlyList<(T Value, string Text)> flat, T? current)
    {
        for (var i = 0; i < flat.Count; i++)
        {
            if (Same(flat[i].Value, current))
            {
                return i;
            }
        }

        return -1;
    }

    private string Display(T? current)
    {
        foreach (var (value, text) in Options)
        {
            if (Same(value, current))
            {
                return text;
            }
        }

        return Placeholder ?? "";
    }

    private T? Current() =>
        Bind is { } bind && ExpressionAccessor.Parse(bind).Getter() is T v ? v : Value;

    private string BoxClass() =>
        UiClass.Compose(
            "select validator",
            Tone is { } tone ? UiClassNames.SelectTone(tone) : "",
            Size is { } size ? UiClassNames.SelectSize(size) : "",
            Variant is { } variant ? UiClassNames.SelectVariant(variant) : "",
            Native == false ? "" : Class);

    // aria-invalid is what makes daisyUI reveal a following UiValidator, and what a screen reader
    // needs: a field that is visibly red and says nothing is half a message. It is OMITTED rather than
    // nulled — a null renders the attribute valueless, and a valueless aria-invalid reads as "true".
    private Dictionary<string, string?> Aria(bool? expanded, string? activeDescendant = null)
    {
        var aria = new Dictionary<string, string?> { ["label"] = Label };
        if (Tone == UiTone.Error)
        {
            aria["invalid"] = "true";
        }

        if (expanded is { } open)
        {
            aria["haspopup"] = "listbox";
            aria["expanded"] = open ? "true" : "false";
            aria["controls"] = ListId;
        }

        if (activeDescendant is { } active)
        {
            aria["activedescendant"] = active;
        }

        return aria;
    }

    private static bool Same(T? a, T? b) => EqualityComparer<T?>.Default.Equals(a, b);

    private static string OptionText(T value) => BindingHelpers.FormatValue(value);
}
