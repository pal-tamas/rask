using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// Shared base for the single-select combobox. Generic over TValue (the bound value) AND TItem (the option),
// so the concrete controls can either bind the option itself (BsSelect<TItem>) or bind a projected field of
// an object option (BsSelect<TValue, TItem> with OptionValue: p => p.Id). Everything else — the .form-select
// display box, the .dropdown-menu listbox, the opt-in dropdown search, nullable × clear, floating label and
// the Native <select> fallback — lives here. The value of an option is obtained through ValueOf().
public abstract class BsSelectBase<TValue, TItem> : BsFormControl<TValue>
{
    public required IEnumerable<TItem> Options { get; set; }

    // Renders each option's content; defaults to item?.ToString() (same shape as BsMultiSelect).
    public Func<TItem, Component>? OptionLabel { get; set; }

    // Shown in the trigger box (custom) / as a leading disabled option (native) when nothing is selected.
    public string? Placeholder { get; set; }

    // The predicate that decides whether an option matches the text typed into the dropdown's search field.
    // Only when it is supplied does the dropdown show a search field and narrow the options; e.g.
    // Filter: (p, text) => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase).
    public Func<TItem, string, bool>? Filter { get; set; }

    // Opt out of the custom popover and render the native <select> instead. Guarantees a working control
    // (and the OS picker on mobile) where the custom UI is unwanted.
    public bool? Native { get; set; }

    // The bound value an option represents — the option itself, or a projected field (see the subclasses).
    private protected abstract TValue ValueOf(TItem item);

    // Selection lives in the bound model / controlled Value; these are pure live-diff view state. _filter is
    // the text the user is currently typing to search (null when not editing → the box shows the value).
    private bool _open;
    private int _cursor;
    private string? _filter;

    private static readonly IEqualityComparer<TValue> Comparer = EqualityComparer<TValue>.Default;
    private static readonly IReadOnlyDictionary<string, string?> SelectedAria =
        new Dictionary<string, string?> { ["selected"] = "true" };

    // A nullable value-type binding (int?/DateOnly?/…) can be cleared back to null; mirrors the pickers'
    // CanClear. Reference types can't be told from their non-nullable form at runtime, so — like the
    // pickers — only Nullable<T> is treated as clearable (a required string/enum select stays value-only).
    // A property (not a cached `static readonly` field) so typeof(TValue) resolves fresh in the correct
    // runtime generic context — a cached generic static mis-resolved under Mono WASM AOT (see BsPickerBase).
    private static bool CanClear => Nullable.GetUnderlyingType(typeof(TValue)) is not null;

    // A per-instance suffix so two id-less selects still emit unique option ids for aria-activedescendant.
    private static int _seq;
    private readonly int _instanceId = System.Threading.Interlocked.Increment(ref _seq);

    protected override Component? Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var b = Resolve();
        var controlId = ControlId(b);
        return Native is true ? RenderNative(b, controlId) : RenderCustom(b, controlId);
    }

    private Component LabelOf(TItem item) =>
        OptionLabel is not null ? OptionLabel(item) : item?.ToString() ?? string.Empty;

    // The native <select>: a plain control fed from Options (each option's value string is the projected
    // value, so binding rides the same StringChangeHandler as every Bs control), with a leading placeholder.
    private Component RenderNative(in Bound b, string? controlId)
    {
        var opts = Options as IReadOnlyList<TItem> ?? Options.ToList();
        var cls = BsClass.Join("form-select", SizeClass("form-select"), b.Invalid ? "is-invalid" : null, Class);

        var children = new List<Component?>();
        // A leading empty option: a non-selectable prompt for a required select, or a selectable "none"
        // entry when the binding is nullable (empty value → null via the shared StringChangeHandler).
        if (Placeholder is not null || CanClear)
        {
            children.Add(Option(Value: "", Disabled: CanClear ? null : true, Key: "placeholder")[
                Placeholder ?? "None"]);
        }

        for (var i = 0; i < opts.Count; i++)
        {
            children.Add(Option(Value: BindingHelpers.FormatValue(ValueOf(opts[i])), Key: i)[LabelOf(opts[i])]);
        }

        var control = Select<string>(
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Disabled: Disabled, Required: Required, Class: cls, Id: controlId, Aria: FieldAria(b, controlId),
            OnChangeAsync: Disabled == true ? null : StringChangeHandler(b))[children];

        return Field(controlId, b, control);
    }

    // The custom dropdown: a .form-select DISPLAY box (rich OptionLabel) that opens a .dropdown-menu listbox
    // + click-outside backdrop. When a Filter predicate is supplied the menu grows a search field at the top
    // (typing narrows the options); with no Filter it is a plain dropdown. Assembled like BsMultiSelect so
    // label/help/invalid-feedback and Floating match. Bound is by value (not `in`) so lambdas can capture it.
    private Component RenderCustom(Bound b, string? controlId)
    {
        var opts = Options as IReadOnlyList<TItem> ?? Options.ToList();
        var disabled = Disabled == true;
        var prefix = controlId ?? "bssel" + _instanceId.ToString(CultureInfo.InvariantCulture);
        var listId = prefix + "-list";
        var selectedIdx = SelectedIndex(b, opts);
        var floating = Floating is true && Label is not null;

        // Filtering is opt-in: only a supplied Filter predicate shows the search field and narrows the list.
        var searchable = Filter is not null;
        var filtered = searchable && !string.IsNullOrEmpty(_filter)
            ? opts.Where(o => Filter!(o, _filter)).ToList()
            : opts;

        // The box shows the selected option's (rich) label, or the muted placeholder; blank while floating+empty.
        Component? content = selectedIdx >= 0
            ? LabelOf(opts[selectedIdx])
            : floating ? null : Span(Class: "text-secondary")[Placeholder ?? "Select…"];

        var aria = new Dictionary<string, string?>
        {
            ["haspopup"] = "listbox",
            ["expanded"] = _open ? "true" : "false",
            ["controls"] = listId,
        };
        if (_open && _cursor >= 0 && _cursor < filtered.Count)
        {
            aria["activedescendant"] = OptId(prefix, _cursor);
        }

        if (FieldAria(b, controlId) is { } fa)
        {
            foreach (var kv in fa)
            {
                aria[kv.Key] = kv.Value;
            }
        }

        // A nullable select with a value gets an × that clears it back to null (matching the pickers). It
        // sits left of the .form-select caret; the box gains right padding so the value never runs under it.
        var showClear = CanClear && selectedIdx >= 0 && !disabled;

        var box = Div(
            Class: BsClass.Join("form-select", showClear ? "bs-select-clearable" : null,
                b.Invalid ? "is-invalid" : null, disabled ? "disabled pe-none" : null),
            Id: controlId,
            Data: BsPopover.Anchor,
            Role: "combobox",
            TabIndex: disabled ? null : 0,
            Aria: aria,
            OnClick: disabled ? null : () => Toggle(b, opts),
            OnKeyDownAsync: disabled ? null : e => OnKeyAsync(b, filtered, e))[content];

        var clear = showClear
            ? BsCloseButton(
                Class: BsClass.Join(Position.Absolute, Position.Top50, Position.TranslateMiddleY, "bs-select-clear"),
                AriaLabel: "Clear",
                OnClickAsync: () => WriteAsync(b, default!))
            : null;

        var rows = new List<Component?>();
        // Opt-in search field pinned at the top of the menu — only rendered while open, so it autofocuses on
        // open; its value is always the typed filter, so the client never fights it.
        if (searchable && _open)
        {
            rows.Add(Div(Class: BsClass.Join("px-2", "pt-1", "pb-2"))[
                Input<string>(
                    Type: InputType.Text,
                    Class: "form-control form-control-sm",
                    Id: prefix + "-search",
                    Value: _filter ?? string.Empty,
                    Placeholder: "Search…",
                    Autocomplete: "off",
                    Autofocus: true,
                    Aria: new Dictionary<string, string?> { ["label"] = "Search" },
                    OnInput: raw => { _filter = raw; _cursor = 0; },
                    OnKeyDownAsync: e => OnKeyAsync(b, filtered, e))]);
        }

        if (searchable && filtered.Count == 0)
        {
            rows.Add(Span(Class: BsClass.Join("dropdown-item", "disabled", Txt.Muted))["No matches"]);
        }
        else
        {
            for (var i = 0; i < filtered.Count; i++)
            {
                var idx = i;
                var item = filtered[i];
                var isSelected = b.Current is not null && Comparer.Equals(ValueOf(item), b.Current);
                rows.Add(Button(
                    Type: "button",
                    Class: BsClass.Join("dropdown-item", isSelected || (_open && idx == _cursor) ? "active" : null),
                    Id: OptId(prefix, idx),
                    Role: "option",
                    Aria: isSelected ? SelectedAria : null,
                    Disabled: Disabled,
                    Key: idx,
                    OnClickAsync: disabled ? null : () => WriteAsync(b, ValueOf(item)))[LabelOf(item)]);
            }
        }

        var menu = Div(
            Id: listId,
            Role: "listbox",
            Class: _open
                ? BsClass.Join("dropdown-menu show", Display.Block(), Sizing.W(100))
                : "dropdown-menu")[rows];

        var labelNode = Label is null
            ? null
            : Rask.Core.Components.Generated.Label(For: controlId, Class: floating ? null : "form-label")[
                Label,
                Required is true ? Span(Class: "text-danger ms-1")["*"] : null];

        var children = new List<Component?>();
        if (labelNode is not null && !floating)
        {
            children.Add(labelNode);
        }

        // Floating wraps box + label in .form-floating (label after → the CSS floats it); the × rides along
        // inside so it anchors to the box. Non-floating: the × is a sibling of the box in the .dropdown
        // (whose menu/backdrop are out of flow, so .dropdown's height is the box's — the × centres on it).
        if (floating)
        {
            children.Add(Div(
                Class: BsClass.Join("form-floating bs-floating",
                    selectedIdx >= 0 ? "bs-floating-filled" : null, Position.Relative))[box, labelNode, clear]);
        }
        else
        {
            children.Add(box);
            children.Add(clear);
        }

        children.Add(menu);

        if (_open && !disabled)
        {
            children.Add(Div(
                Class: BsClass.Join(Position.Fixed, Position.Top0, Position.Start0, Sizing.W(100), Sizing.H(100)),
                Style: "z-index: 999;",
                OnClick: CloseAndReset));
        }

        if (HelpText is not null)
        {
            children.Add(Div(Id: HelpTextId(controlId), Class: "form-text")[HelpText]);
        }

        if (b.Invalid)
        {
            children.Add(Div(Id: ErrorId(controlId, b), Class: "invalid-feedback d-block", Role: "alert")[b.Messages[0]]);
        }

        return Div(Class: BsClass.Join("dropdown", Class), Data: BsPopover.Wrapper)[children];
    }

    // Clicking the display box toggles the popover; opening seeds the keyboard cursor to the selected option.
    private void Toggle(Bound b, IReadOnlyList<TItem> opts)
    {
        if (_open)
        {
            CloseAndReset();
            return;
        }

        var s = SelectedIndex(b, opts);
        _cursor = s >= 0 ? s : 0;
        _open = true;
    }

    private static string OptId(string prefix, int idx) =>
        prefix + "-opt-" + idx.ToString(CultureInfo.InvariantCulture);

    // Index of the option whose projected value equals the bound value, or -1.
    private int SelectedIndex(in Bound b, IReadOnlyList<TItem> opts)
    {
        if (b.Current is null)
        {
            return -1;
        }

        for (var i = 0; i < opts.Count; i++)
        {
            if (Comparer.Equals(ValueOf(opts[i]), b.Current))
            {
                return i;
            }
        }

        return -1;
    }

    // Closes the popover and drops the in-progress filter so the box reverts to the selected value's text.
    private void CloseAndReset()
    {
        _open = false;
        _filter = null;
    }

    // Combobox keyboard over the FILTERED list: arrows move the cursor, Home/End jump, Enter picks the
    // cursor, Escape closes. Space is left to type into the filter. Focus already opened the popover.
    private async Task OnKeyAsync(Bound b, IReadOnlyList<TItem> filtered, KeyboardEventArgs e)
    {
        if (!_open)
        {
            if (e.Key is "ArrowDown" or "ArrowUp" or "Enter" or " ")
            {
                var s = SelectedIndex(b, filtered);
                _cursor = s >= 0 ? s : 0;
                _open = true;
            }

            return;
        }

        switch (e.Key)
        {
            case "Escape":
                CloseAndReset();
                break;
            case "ArrowDown":
                _cursor = Math.Min(_cursor + 1, filtered.Count - 1);
                break;
            case "ArrowUp":
                _cursor = Math.Max(_cursor - 1, 0);
                break;
            case "Home":
                _cursor = 0;
                break;
            case "End":
                _cursor = filtered.Count - 1;
                break;
            case "Enter":
                if (_cursor >= 0 && _cursor < filtered.Count)
                {
                    await WriteAsync(b, ValueOf(filtered[_cursor])).ConfigureAwait(false);
                }

                break;
        }
    }

    // Writes the chosen value back to the model (bound) or notifies the parent (controlled), then closes and
    // drops the filter so the box shows the picked value. A default(TValue) is the clear (× / nullable).
    private async Task WriteAsync(Bound b, TValue value)
    {
        _open = false;
        _filter = null;
        if (b.Accessor is { } acc)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(b.Context, b.Field).ConfigureAwait(false);
            await ((IFormControl<TValue>)this).InvokeAfterBindAsync(value).ConfigureAwait(false);
        }
        else
        {
            await ((IFormControl<TValue>)this).InvokeOnChangeAsync(value).ConfigureAwait(false);
        }
    }
}

// A Bootstrap single-select bound to the option itself — the single-value twin of BsMultiSelect. Renders a
// custom .form-select combobox by default (Options + OptionLabel; opt-in dropdown search via Filter; nullable
// × clear; floating label); Native: true degrades to the plain OS <select>.
//   BsSelect(() => model.Plan, plans, OptionLabel: p => Text(p), Filter: (p, t) => p.Contains(t, …))
public sealed class BsSelect<TItem> : BsSelectBase<TItem, TItem>
{
    private protected override TItem ValueOf(TItem item) => item;
}

// A Bootstrap single-select whose Options are objects but whose bound value is a projected field, chosen by
// OptionValue — so you can bind an id while rendering/searching the whole object.
//   BsSelect(() => model.PersonId, people, OptionValue: p => p.Id, OptionLabel: p => Text(p.Name))
public sealed class BsSelect<TValue, TItem> : BsSelectBase<TValue, TItem>
{
    // Projects an option to the value bound to the model (e.g. p => p.Id).
    public required Func<TItem, TValue> OptionValue { get; set; }

    private protected override TValue ValueOf(TItem item) => OptionValue(item);
}
