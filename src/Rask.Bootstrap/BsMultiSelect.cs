using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap multiselect: a dropdown of checkable options with the chosen items shown as removable
// chips, bound to an ICollection<TItem>. Implements IFormControl<ICollection<TItem>> (bound +
// controlled). Open/close, the click-outside backdrop and Esc-to-close are pure live-diff state — no
// bootstrap.js. The chips reuse BsBadge + BsCloseButton.
public sealed partial class BsMultiSelect<TItem> : BsBlock, IFormControl<ICollection<TItem>>
{
    public required IEnumerable<TItem> Options { get; set; }

    // Controlled mode (no Bind).
    public ICollection<TItem>? Value { get; set; }
    public Callback<ICollection<TItem>>? OnChange { get; set; }
    public CallbackAsync<ICollection<TItem>>? OnChangeAsync { get; set; }

    // Bound mode (IFormControl members).
    public Expression<Func<ICollection<TItem>>>? Bind { get; set; }
    public Carrier<Validate<ICollection<TItem>>>? Validate { get; set; }
    public Carrier<ValidateAsync<ICollection<TItem>>>? ValidateAsync { get; set; }
    public Carrier<Action<ICollection<TItem>>>? AfterBind { get; set; }
    public Carrier<Func<ICollection<TItem>, Task>>? AfterBindAsync { get; set; }

    public Func<TItem, Component>? OptionLabel { get; set; }
    public string? Placeholder { get; set; }
    public bool? Disabled { get; set; }

    // Marks individual options non-selectable. A disabled option renders greyed (aria-disabled), takes no
    // click, and the keyboard cursor skips over it; e.g. OptionDisabled: t => t.Retired.
    public Func<TItem, bool>? OptionDisabled { get; set; }

    // The predicate that decides whether an option matches the text typed into the dropdown's search field.
    // Only when it is supplied does the dropdown show a search field and narrow the options; e.g.
    // Filter: (t, text) => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase).
    public new Func<TItem, string, bool>? Filter { get; set; }

    // Opt in to a "Select all / Clear all" header row at the top of the dropdown. It toggles the currently
    // shown (filtered), enabled options in one click — adds them all, or clears them when they are already
    // all selected — never touching a disabled option. With a Filter active it applies to the visible subset.
    public bool? SelectAll { get; set; }

    // Groups the options under non-interactive .dropdown-header rows, keyed by the returned string in first-seen
    // order; e.g. OptionGroup: t => t.Category. The roving cursor still walks the flat option order.
    public Func<TItem, string>? OptionGroup { get; set; }

    // Optional field label. Floating wraps the control + label in a .form-floating (the .form-select
    // control box makes Bootstrap float the label just like a native select); otherwise it sits above.
    public new string? Label { get; set; }
    public bool? Floating { get; set; }

    // View state only — the selection lives in the bound model / parent Value. Toggling re-renders. _filter
    // is the text typed into the inline search field (null when not searching); _cursor is the roving
    // keyboard highlight (a flat index into the filtered option list), surfaced as aria-activedescendant.
    private bool _open;
    private string? _filter;
    private int _cursor;

    // A per-instance suffix so two id-less multiselects still emit unique list/label ids for the
    // combobox aria-controls / aria-labelledby wiring. Uses the shared non-generic counter so two
    // id-less multiselects of different TItem don't both start at 1 and collide.
    private readonly int _instanceId = BsInstanceId.Next();

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

        // Group (optional) and flatten: `flat` is the option order the roving cursor indexes — grouping only
        // reorders it into first-seen group order, so flat index still equals the rendered option position.
        var layout = BsSelectNav.Build(filteredList, OptionGroup);
        var flat = layout.Flat;

        // Per-option disable predicate over the flat list; the keyboard cursor skips these indices and the
        // option row takes no click.
        Func<int, bool> optDisabled = i => OptionDisabled?.Invoke(flat[i]) == true;

        // The roving keyboard cursor lives in flat option-index space. Normalise it once against the current
        // list so a filter change (which may shrink the list) can't leave it dangling past the end, and seed
        // it to the first selected option so opening lands the highlight where the eye already is.
        if (_open)
        {
            _cursor = BsSelectNav.Normalize(_cursor, flat.Count, optDisabled);
        }

        var firstSelected = -1;
        for (var i = 0; i < flat.Count; i++)
        {
            if (selected is not null && selected.Contains(flat[i], comparer))
            {
                firstSelected = i;
                break;
            }
        }

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
                Rask.Core.Components.Generated.Input<string>(
                    Type: InputType.Text,
                    Class: "form-control form-control-sm",
                    Value: _filter ?? string.Empty,
                    Placeholder: "Search…",
                    Autocomplete: "off",
                    Autofocus: true,
                    Aria: new Dictionary<string, string?> { ["label"] = "Search" },
                    OnInput: raw => { _filter = raw; _cursor = 0; },
                    OnKeyDownAsync: e => OnKeyAsync(e, fromSearch: true))]);
        }

        // Opt-in "Select all / Clear all" header: toggles every shown, ENABLED option in one click (never a
        // disabled one). Shows "Clear all" once they are all selected. Not part of the roving-cursor option
        // space (it is a bulk-action button, not a role="option"), so arrow keys skip it.
        if (SelectAll is true && !disabled)
        {
            var enabledFiltered = new List<TItem>();
            for (var i = 0; i < flat.Count; i++)
            {
                if (!optDisabled(i))
                {
                    enabledFiltered.Add(flat[i]);
                }
            }

            if (enabledFiltered.Count > 0)
            {
                var allOn = enabledFiltered.All(o => selected is not null && selected.Contains(o, comparer));
                rows.Add(Button(
                    Type: "button",
                    Class: "dropdown-item d-flex align-items-center gap-2 fw-semibold",
                    OnClickAsync: () => SelectAllAsync(acc, ctx, fid, enabledFiltered, comparer, add: !allOn))[
                    allOn ? "Clear all" : "Select all"]);
            }
        }

        if (searchable && flat.Count == 0)
        {
            rows.Add(Span(Class: BsClass.Join("dropdown-item", "disabled", Txt.Muted))["No matches"]);
        }
        else
        {
            // Walk the groups: a non-interactive .dropdown-header per group (skipped by the cursor), then that
            // group's option rows keyed by their FLAT index so ids/active/aria-activedescendant stay in step.
            foreach (var g in layout.Groups)
            {
                if (g.Header is not null)
                {
                    rows.Add(Div(Class: "dropdown-header", Key: "hdr-" + g.Header)[g.Header]);
                }

                foreach (var row in g.Rows)
                {
                    var idx = row.FlatIndex;
                    var captured = row.Item;
                    var isChecked = selected is not null && selected.Contains(captured, comparer);
                    var optionDisabled = disabled || optDisabled(idx);
                    // aria-selected and the read-only checkbox both derive from isChecked (never from _cursor),
                    // so they can't drift; the cursor is surfaced only as the .active highlight. role="option" +
                    // aria-selected make this a proper listbox option for assistive tech; a per-option-disabled
                    // row adds aria-disabled and drops its click handler.
                    var optAria = new Dictionary<string, string?> { ["selected"] = isChecked ? "true" : "false" };
                    if (optionDisabled)
                    {
                        optAria["disabled"] = "true";
                    }

                    rows.Add(Button(
                        Type: "button",
                        Class: BsClass.Join("dropdown-item d-flex align-items-center gap-2",
                            _open && idx == _cursor ? "active" : null),
                        Id: BsSelectNav.OptId(prefix, idx),
                        Role: "option",
                        Aria: optAria,
                        Disabled: optionDisabled ? true : null,
                        OnClickAsync: optionDisabled
                            ? null
                            : () => ToggleAsync(acc, ctx, fid, captured, comparer, add: !isChecked),
                        Key: idx)[
                        Rask.Core.Components.Generated.Input<string>(InputType.Checkbox, Class: "form-check-input m-0 pe-none", Checked: isChecked),
                        LabelOf(captured)
                    ]);
                }
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

        if (_open && _cursor >= 0 && _cursor < flat.Count)
        {
            boxAria["activedescendant"] = BsSelectNav.OptId(prefix, _cursor);
        }

        // Merge the shared aria-invalid/aria-describedby contract (the same builder BsSelect/BsFormControl
        // use) so the four controls stay in lockstep if it ever grows.
        if (BsClass.FieldAria(invalid, errorId) is { } fa)
        {
            foreach (var kv in fa)
            {
                boxAria[kv.Key] = kv.Value;
            }
        }

        var boxDiv = Div(
            Class: BsClass.Join("form-select", Sizing.HAuto, Display.Flex(), Flex.Wrap(),
                Flex.Align(BsAlign.Center), Flex.Gap(1), invalid ? "is-invalid" : null,
                disabled ? "disabled pe-none" : null),
            Data: BsPopover.Anchor,
            Role: "combobox",
            TabIndex: disabled ? null : 0,
            Aria: boxAria,
            OnClick: disabled ? null : () =>
            {
                if (_open)
                {
                    _open = false;
                    _filter = null;
                }
                else
                {
                    _cursor = BsSelectNav.Seed(firstSelected, flat.Count, optDisabled);
                    _open = true;
                }
            },
            OnKeyDownAsync: disabled ? null : e => OnKeyAsync(e, fromSearch: false))[box];

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
        children.Add(Div(Id: listId, Role: "listbox",
            Aria: new Dictionary<string, string?> { ["multiselectable"] = "true" },
            Class: _open
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

        // Combobox keyboard over the FILTERED list, mirroring BsSelect's OnKeyAsync: arrows move the roving
        // cursor (skipping disabled options), Home/End jump, Enter/Space toggle the cursor option's membership,
        // Escape closes. A local function so it captures this render's binding state (acc/ctx/fid/selected/…),
        // exactly as the chip-remove and option-click handlers do. `fromSearch` is true when the event comes
        // from the in-dropdown search field, where Space must type a literal space instead of toggling.
        async Task OnKeyAsync(KeyboardEventArgs e, bool fromSearch)
        {
            var count = flat.Count;
            if (!_open)
            {
                if (e.Key is "ArrowDown" or "ArrowUp" or "Enter" or " ")
                {
                    _cursor = BsSelectNav.Seed(firstSelected, count, optDisabled);
                    _open = true;
                }

                return;
            }

            switch (e.Key)
            {
                case "Escape":
                    _open = false;
                    _filter = null;
                    break;
                case "ArrowDown":
                    _cursor = BsSelectNav.Step(_cursor, 1, count, optDisabled);
                    break;
                case "ArrowUp":
                    _cursor = BsSelectNav.Step(_cursor, -1, count, optDisabled);
                    break;
                case "Home":
                    _cursor = BsSelectNav.FirstEnabled(count, optDisabled);
                    break;
                case "End":
                    _cursor = BsSelectNav.LastEnabled(count, optDisabled);
                    break;
                case "Enter":
                case " " when !fromSearch:
                    if (_cursor >= 0 && _cursor < count && !optDisabled(_cursor))
                    {
                        var item = flat[_cursor];
                        var isChecked = selected is not null && selected.Contains(item, comparer);
                        await ToggleAsync(acc, ctx, fid, item, comparer, add: !isChecked).ConfigureAwait(false);
                    }

                    break;
            }
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

    // Bulk add/remove for the "Select all / Clear all" header — applies membership to every item then notifies
    // once (bound: mutate the model collection in place; controlled: emit a fresh list), mirroring ToggleAsync.
    private async Task SelectAllAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        IReadOnlyList<TItem> items, IEqualityComparer<TItem> comparer, bool add)
    {
        if (acc is not null)
        {
            if (acc.Getter() is not ICollection<TItem> collection)
            {
                return;
            }

            foreach (var item in items)
            {
                BindingHelpers.SetCollectionMembership(collection, item, add, comparer);
            }

            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            await ((IFormControl<ICollection<TItem>>)this).InvokeAfterBindAsync(collection).ConfigureAwait(false);
        }
        else
        {
            var next = Value is null ? new List<TItem>() : new List<TItem>(Value);
            foreach (var item in items)
            {
                BindingHelpers.SetCollectionMembership(next, item, add, comparer);
            }

            await ((IFormControl<ICollection<TItem>>)this).InvokeOnChangeAsync(next).ConfigureAwait(false);
        }
    }
}
