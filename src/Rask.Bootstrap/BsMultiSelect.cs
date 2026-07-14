using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap multiselect: a dropdown of checkable options with the chosen items shown as removable
// chips, bound to an ICollection<TItem>. Implements IFormControl<ICollection<TItem>> (bound +
// controlled). Open/close, the click-outside backdrop and Esc-to-close are pure live-diff state — no
// bootstrap.js. The chips reuse BsBadge + BsCloseButton.
public sealed class BsMultiSelect<TItem> : BsBlock, IFormControl<ICollection<TItem>>
{
    public required IEnumerable<TItem> Options { get; set; }

    // Controlled mode (no Bind).
    public ICollection<TItem>? Value { get; set; }
    public Callback<ICollection<TItem>>? OnChange { get; set; }
    public CallbackAsync<ICollection<TItem>>? OnChangeAsync { get; set; }

    // Bound mode (IFormControl members).
    public Expression<Func<ICollection<TItem>>>? Bind { get; set; }
    public Validate<ICollection<TItem>>? Validate { get; set; }
    public ValidateAsync<ICollection<TItem>>? ValidateAsync { get; set; }
    public Action<ICollection<TItem>>? AfterBind { get; set; }
    public Func<ICollection<TItem>, Task>? AfterBindAsync { get; set; }

    public Func<TItem, Component>? OptionLabel { get; set; }
    public string? Placeholder { get; set; }
    public bool? Disabled { get; set; }

    // The predicate that decides whether an option matches the text typed into the dropdown's search field.
    // Only when it is supplied does the dropdown show a search field and narrow the options; e.g.
    // Filter: (t, text) => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase).
    public Func<TItem, string, bool>? Filter { get; set; }

    // Optional field label. Floating wraps the control + label in a .form-floating (the .form-select
    // control box makes Bootstrap float the label just like a native select); otherwise it sits above.
    public string? Label { get; set; }
    public bool? Floating { get; set; }

    // View state only — the selection lives in the bound model / parent Value. Toggling re-renders. _filter
    // is the text typed into the inline search field (null when not searching).
    private bool _open;
    private string? _filter;

    // A per-instance suffix so two id-less multiselects still emit unique list/label ids for the
    // combobox aria-controls / aria-labelledby wiring (mirrors BsSelectBase).
    private static int _seq;
    private readonly int _instanceId = System.Threading.Interlocked.Increment(ref _seq);

    protected override Component? Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var bound = Bind is not null;
        if (bound == Value is not null)
        {
            throw new InvalidOperationException(
                "BsMultiSelect requires exactly one of Bind (bound mode) or Value (controlled mode).");
        }

        var comparer = EqualityComparer<TItem>.Default;
        var disabled = Disabled == true;

        ExpressionAccessor.Accessor? acc = null;
        EditContext? ctx = null;
        var fid = default(FieldIdentifier);
        ICollection<TItem>? selected;
        if (bound)
        {
            acc = ExpressionAccessor.Parse(Bind!);
            ctx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            ((IFormControl<ICollection<TItem>>)this).RegisterValidator(acc, ctx);
            selected = acc.Getter() as ICollection<TItem>;
        }
        else
        {
            selected = Value;
        }

        // Stable ids for the combobox aria wiring: aria-controls → the listbox menu, aria-labelledby → the
        // visible label (the box is a <div role="combobox">, not a labelable element, so <label for> is void),
        // and aria-describedby → the error feedback. Reading GetValidationMessages here auto-latches the
        // render-cache opt-out (see BsFormControl), so a message added during submit repaints the box's
        // aria-invalid instead of being served stale.
        var prefix = Id ?? acc?.PropertyName ?? "bsms" + _instanceId.ToString(CultureInfo.InvariantCulture);
        var listId = prefix + "-list";
        var labelId = Label is not null ? prefix + "-label" : null;
        IReadOnlyList<string> messages = bound && ctx is not null ? ctx.GetValidationMessages(fid) : [];
        var invalid = messages.Count > 0;
        var errorId = invalid ? prefix + "-error" : null;

        Component LabelOf(TItem item) =>
            OptionLabel is not null ? OptionLabel(item) : item?.ToString() ?? string.Empty;

        // Filtering is opt-in: only a supplied Filter predicate shows the dropdown's search field and narrows
        // the options by what the user has typed.
        var searchable = Filter is not null;
        var filtered = searchable && !string.IsNullOrEmpty(_filter)
            ? Options.Where(o => Filter!(o, _filter))
            : Options;
        var filteredList = filtered as IReadOnlyList<TItem> ?? filtered.ToList();

        var hasChips = selected is not null && selected.Count > 0;
        var floating = Floating is true && Label is not null;

        // The control box holds the selected chips (BsBadge + BsCloseButton), or a placeholder when empty.
        // While floating + empty the box stays blank: the centred floating label acts as the placeholder
        // (matching BsSelect and native .form-floating), so a leftover "Select…" span can't overlap it.
        var box = new List<Component>();
        if (hasChips)
        {
            var i = 0;
            foreach (var item in selected!)
            {
                var captured = item;
                box.Add(BsBadge(Color: BsColor.Primary, Class: "d-inline-flex align-items-center", Key: i)[
                    LabelOf(captured),
                    BsCloseButton(White: true, Class: "ms-1", Disabled: Disabled,
                        OnClickAsync: disabled ? null : () => ToggleAsync(acc, ctx, fid, captured, comparer, add: false))
                ]);
                i++;
            }
        }
        else if (!floating)
        {
            box.Add(Span(Class: "text-secondary")[Placeholder ?? "Select…"]);
        }

        var rows = new List<Component?>();
        // Opt-in search field pinned at the top of the menu — only while open, so it autofocuses on open.
        if (searchable && _open)
        {
            rows.Add(Div(Class: BsClass.Join("px-2", "pt-1", "pb-2"))[
                Input<string>(
                    Type: InputType.Text,
                    Class: "form-control form-control-sm",
                    Value: _filter ?? string.Empty,
                    Placeholder: "Search…",
                    Autocomplete: "off",
                    Autofocus: true,
                    Aria: new Dictionary<string, string?> { ["label"] = "Search" },
                    OnInput: raw => _filter = raw,
                    OnKeyDown: OnBoxKeyDown)]);
        }

        if (searchable && filteredList.Count == 0)
        {
            rows.Add(Span(Class: BsClass.Join("dropdown-item", "disabled", Txt.Muted))["No matches"]);
        }
        else
        {
            var idx = 0;
            foreach (var option in filteredList)
            {
                var captured = option;
                var isChecked = selected is not null && selected.Contains(captured, comparer);
                rows.Add(Button(
                    Type: "button", Class: "dropdown-item d-flex align-items-center gap-2", Disabled: Disabled,
                    OnClickAsync: disabled ? null : () => ToggleAsync(acc, ctx, fid, captured, comparer, add: !isChecked),
                    Key: idx)[
                    Input<string>(InputType.Checkbox, Class: "form-check-input m-0 pe-none", Checked: isChecked),
                    LabelOf(captured)
                ]);
                idx++;
            }
        }

        var boxAria = new Dictionary<string, string?>
        {
            ["haspopup"] = "listbox",
            ["expanded"] = _open ? "true" : "false",
            ["controls"] = listId,
        };
        if (labelId is not null)
        {
            boxAria["labelledby"] = labelId;
        }

        if (invalid)
        {
            boxAria["invalid"] = "true";
            boxAria["describedby"] = errorId;
        }

        var boxDiv = Div(
            Class: BsClass.Join("form-select", Sizing.HAuto, Display.Flex(), Flex.Wrap(),
                Flex.Align(BsAlign.Center), Flex.Gap(1), invalid ? "is-invalid" : null,
                disabled ? "disabled pe-none" : null),
            Data: BsPopover.Anchor,
            Role: "combobox",
            TabIndex: disabled ? null : 0,
            Aria: boxAria,
            OnClick: disabled ? null : () => { _open = !_open; if (!_open) { _filter = null; } },
            OnKeyDown: disabled ? null : OnBoxKeyDown)[box];

        var labelNode = Label is null
            ? null
            : Rask.Core.Components.Generated.Label(Id: labelId, Class: floating ? null : "form-label")[Label];

        var children = new List<Component?>();
        if (labelNode is not null && !floating)
        {
            children.Add(labelNode);
        }

        // Floating: .form-floating.bs-floating wraps the .form-select box + label; the label floats when
        // there are chips (.bs-floating-filled) or while the search field is focused (:focus-within). The
        // dropdown menu stays a sibling inside .dropdown so it still positions correctly.
        children.Add(floating
            ? Div(Class: BsClass.Join("form-floating bs-floating", hasChips ? "bs-floating-filled" : null,
                Position.Relative))[boxDiv, labelNode]
            : boxDiv);
        children.Add(Div(Id: listId, Role: "listbox", Class: _open
            ? BsClass.Join("dropdown-menu show", Display.Block(), Sizing.W(100))
            : "dropdown-menu")[rows]);

        if (_open && !disabled)
        {
            children.Add(Div(
                Class: BsClass.Join(Position.Fixed, Position.Top0, Position.Start0, Sizing.W(100), Sizing.H(100)),
                Style: "z-index: 999;",
                OnClick: () => { _open = false; _filter = null; }));
        }

        // The error is a role="alert" live region carrying the id the box's aria-describedby points at, so a
        // screen reader announces it on submit/blur associated with — not detached from — the control. Read
        // directly from the messages resolved above (which latched the cache opt-out) rather than via a
        // headless ValidationMessage, so the error id and the box's aria-describedby stay in lockstep.
        if (invalid)
        {
            children.Add(Div(Id: errorId, Class: BsClass.Join("invalid-feedback", Display.Block()),
                Role: "alert")[messages[0]]);
        }

        return Div(Class: BsClass.Join("dropdown", Class), Id: Id, Data: BsPopover.Wrapper)[children];
    }

    private void OnBoxKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _open = false;
            _filter = null;
        }
    }

    private async Task ToggleAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TItem item, IEqualityComparer<TItem> comparer, bool add)
    {
        if (acc is not null)
        {
            if (acc.Getter() is not ICollection<TItem> collection)
            {
                return;
            }

            BindingHelpers.SetCollectionMembership(collection, item, add, comparer);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            await ((IFormControl<ICollection<TItem>>)this).InvokeAfterBindAsync(collection).ConfigureAwait(false);
        }
        else
        {
            var next = Value is null ? new List<TItem>() : new List<TItem>(Value);
            BindingHelpers.SetCollectionMembership(next, item, add, comparer);
            await ((IFormControl<ICollection<TItem>>)this).InvokeOnChangeAsync(next).ConfigureAwait(false);
        }
    }
}
