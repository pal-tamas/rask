using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap single-select: the single-value twin of BsMultiSelect. By default it renders a custom
// dropdown — a .form-select-styled box that opens a .dropdown-menu listbox of options — so it matches the
// multiselect and the date/time pickers. Open/close, the click-outside backdrop and Esc/arrow-key
// navigation are pure live-diff view state (no bootstrap.js); the menu re-anchors via BsPopover. Data
// driven like BsMultiSelect (Options + OptionLabel), bound or controlled through IFormControl<TItem>.
// Native:true degrades to the plain native <select> (BsSelect(() => model.Plan, plans, Native: true)).
public sealed class BsSelect<TItem> : BsFormControl<TItem>
{
    public required IEnumerable<TItem> Options { get; set; }

    // Renders each option's content; defaults to item?.ToString() (same shape as BsMultiSelect).
    public Func<TItem, Component>? OptionLabel { get; set; }

    // Shown in the trigger box (custom) / as a leading disabled option (native) when nothing is selected.
    public string? Placeholder { get; set; }

    // Opt out of the custom popover and render the native <select> instead. Guarantees a working control
    // (and the OS picker on mobile) where the custom UI is unwanted.
    public bool? Native { get; set; }

    // Selection lives in the bound model / controlled Value; these are pure live-diff view state.
    private bool _open;
    private int _cursor;

    private static readonly IEqualityComparer<TItem> Comparer = EqualityComparer<TItem>.Default;
    private static readonly IReadOnlyDictionary<string, string?> SelectedAria =
        new Dictionary<string, string?> { ["selected"] = "true" };

    // A nullable value-type binding (int?/DateOnly?/…) can be cleared back to null; mirrors the pickers'
    // CanClear. Reference types can't be told from their non-nullable form at runtime, so — like the
    // pickers — only Nullable<T> is treated as clearable (a required string/enum select stays value-only).
    // A property (not a cached `static readonly` field) so typeof(TItem) resolves fresh in the correct
    // runtime generic context — a cached generic static mis-resolved under Mono WASM AOT (see BsPickerBase).
    private static bool CanClear => Nullable.GetUnderlyingType(typeof(TItem)) is not null;

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

    // The native <select>: today's plain control, fed from Options (with a leading disabled placeholder
    // option) instead of Option children. Binding rides the same StringChangeHandler as every Bs control.
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
            children.Add(Option(Value: BindingHelpers.FormatValue(opts[i]), Key: i)[LabelOf(opts[i])]);
        }

        var control = Select<string>(
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Disabled: Disabled, Required: Required, Class: cls, Id: controlId, Aria: FieldAria(b, controlId),
            OnChangeAsync: Disabled == true ? null : StringChangeHandler(b))[children];

        return Field(controlId, b, control);
    }

    // The custom dropdown: a .form-select combobox box + a .dropdown-menu listbox + click-outside backdrop,
    // assembled like BsMultiSelect (so the label/help/invalid-feedback and Floating come out identical).
    // Bound is taken by value (not `in`) so the box/menu event lambdas can capture it.
    private Component RenderCustom(Bound b, string? controlId)
    {
        var opts = Options as IReadOnlyList<TItem> ?? Options.ToList();
        var disabled = Disabled == true;
        var prefix = controlId ?? "bssel" + _instanceId.ToString(CultureInfo.InvariantCulture);
        var listId = prefix + "-list";
        var selectedIdx = SelectedIndex(b, opts);
        var floating = Floating is true && Label is not null;

        // Floating shows the label as the in-box placeholder while empty, so the box itself stays blank
        // (no separate placeholder text); otherwise a muted placeholder span fills the empty box.
        Component? content = selectedIdx >= 0
            ? LabelOf(opts[selectedIdx])
            : floating ? null : Span(Class: "text-secondary")[Placeholder ?? "Select…"];

        var aria = new Dictionary<string, string?>
        {
            ["haspopup"] = "listbox",
            ["expanded"] = _open ? "true" : "false",
            ["controls"] = listId,
        };
        if (_open && _cursor >= 0 && _cursor < opts.Count)
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
            OnKeyDownAsync: disabled ? null : e => OnKeyAsync(b, opts, e))[content];

        var clear = showClear
            ? BsCloseButton(
                Class: BsClass.Join(Position.Absolute, Position.Top50, Position.TranslateMiddleY, "bs-select-clear"),
                AriaLabel: "Clear",
                OnClickAsync: () => PickAsync(b, default!))
            : null;

        var rows = new List<Component>();
        for (var i = 0; i < opts.Count; i++)
        {
            var idx = i;
            var item = opts[i];
            var isSelected = idx == selectedIdx;
            rows.Add(Button(
                Type: "button",
                Class: BsClass.Join("dropdown-item", isSelected || (_open && idx == _cursor) ? "active" : null),
                Id: OptId(prefix, idx),
                Role: "option",
                Aria: isSelected ? SelectedAria : null,
                Disabled: Disabled,
                Key: idx,
                OnClickAsync: disabled ? null : () => PickAsync(b, item))[LabelOf(item)]);
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
                OnClick: () => _open = false));
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

    private static string OptId(string prefix, int idx) =>
        prefix + "-opt-" + idx.ToString(CultureInfo.InvariantCulture);

    private static int SelectedIndex(in Bound b, IReadOnlyList<TItem> opts)
    {
        if (b.Current is null)
        {
            return -1;
        }

        for (var i = 0; i < opts.Count; i++)
        {
            if (Comparer.Equals(opts[i], b.Current))
            {
                return i;
            }
        }

        return -1;
    }

    private void Toggle(Bound b, IReadOnlyList<TItem> opts)
    {
        if (!_open)
        {
            var s = SelectedIndex(b, opts);
            _cursor = s >= 0 ? s : 0;
        }

        _open = !_open;
    }

    // Combobox keyboard: closed → Enter/Space/Arrow opens and seeds the cursor; open → arrows move the
    // cursor over the options, Home/End jump, Enter/Space pick it, Escape closes.
    private async Task OnKeyAsync(Bound b, IReadOnlyList<TItem> opts, KeyboardEventArgs e)
    {
        if (!_open)
        {
            if (e.Key is "Enter" or " " or "ArrowDown" or "ArrowUp")
            {
                var s = SelectedIndex(b, opts);
                _cursor = s >= 0 ? s : 0;
                _open = true;
            }

            return;
        }

        switch (e.Key)
        {
            case "Escape":
                _open = false;
                break;
            case "ArrowDown":
                _cursor = Math.Min(_cursor + 1, opts.Count - 1);
                break;
            case "ArrowUp":
                _cursor = Math.Max(_cursor - 1, 0);
                break;
            case "Home":
                _cursor = 0;
                break;
            case "End":
                _cursor = opts.Count - 1;
                break;
            case "Enter":
            case " ":
                if (_cursor >= 0 && _cursor < opts.Count)
                {
                    await PickAsync(b, opts[_cursor]).ConfigureAwait(false);
                }

                break;
        }
    }

    // Writes the chosen item back to the model (bound) or notifies the parent (controlled), then closes.
    private async Task PickAsync(Bound b, TItem item)
    {
        _open = false;
        if (b.Accessor is { } acc)
        {
            acc.Setter(item);
            await BindingHelpers.NotifyAndValidateFieldAsync(b.Context, b.Field).ConfigureAwait(false);
            await ((IFormControl<TItem>)this).InvokeAfterBindAsync(item).ConfigureAwait(false);
        }
        else
        {
            await ((IFormControl<TItem>)this).InvokeOnChangeAsync(item).ConfigureAwait(false);
        }
    }
}
