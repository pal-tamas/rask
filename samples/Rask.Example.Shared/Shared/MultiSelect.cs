using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Example.Shared;

// Generic multiselect: a custom Bootstrap dropdown of checkable options with the chosen items shown as
// removable chips. Unlike the native <select multiple>, it binds to an ICollection<TItem> and drives the
// ambient EditContext (validation) — the same contract as CheckboxGroup<TItem>. Open/close, the
// click-outside backdrop and Esc-to-close are all pure server live-diff state (no client JS; the showcase
// loads Bootstrap CSS only). It demonstrates the public binding API: ExpressionAccessor.Parse +
// BindingHelpers.ResolveBindingContext are all a custom form control needs to bind to a model, register a
// per-field validator and report changes.
//
// Two usage shapes, mirroring Input:
//   • Bound    — MultiSelect<string>(() => model.Interests, options, Validate: …, AfterBind: …)
//                two-way binds the model collection, runs the per-field Validate rule and surfaces its
//                message through the embedded ValidationMessage. AfterBind/AfterBindAsync are post-bind
//                side-effect hooks (the bound value is passed in), exactly like Input's AfterBind.
//   • Controlled — MultiSelect<string>(options, Value: selection, OnChange: next => …)
//                the parent owns the selection; OnChange/OnChangeAsync deliver the new collection. No
//                EditContext, so no Validate in this mode.
//
// Live feedback in bound mode lives inside the control (the chips and the embedded ValidationMessage),
// which refresh because MultiSelect re-renders itself. In controlled mode OnChange/OnChangeAsync are
// auto-wrapped (AutoCallback) so invoking them re-renders the consumer that owns the handler — host-side
// derived UI (a summary) updates for free, no StateHasChanged. AfterBind exists for consumer logic.
// Implements IFormControl<ICollection<TItem>> so the factory generator synthesizes both the controlled
// factory (Options, Value, OnChange, …) and the Bind-first bound factory (with the validator fanned into
// none/sync/async). No hand-written Bound method, no [SkipFactory] — the generator excludes the bound-mode
// members from the controlled factory by interface.
public sealed class MultiSelect<TItem> : Component, IFormControl<ICollection<TItem>>
{
    public required IEnumerable<TItem> Options { get; set; }

    // Controlled mode (no Bind): the parent owns the selection and is notified of every change.
    public ICollection<TItem>? Value { get; set; }
    public Callback<ICollection<TItem>>? OnChange { get; set; }
    public CallbackAsync<ICollection<TItem>>? OnChangeAsync { get; set; }

    // Bound mode (IFormControl members): two-way binds the model collection and runs the per-field rule.
    public Expression<Func<ICollection<TItem>>>? Bind { get; set; }
    public Validate<ICollection<TItem>>? Validate { get; set; }
    public ValidateAsync<ICollection<TItem>>? ValidateAsync { get; set; }
    public Action<ICollection<TItem>>? AfterBind { get; set; }
    public Func<ICollection<TItem>, Task>? AfterBindAsync { get; set; }

    public Func<TItem, Child>? OptionLabel { get; set; }
    public string? Id { get; set; }
    public string? Placeholder { get; set; }
    public bool? Disabled { get; set; }

    // View state only — the selection itself lives in the bound model collection (bound mode) or the
    // parent's Value (controlled mode). Toggling open/close re-renders this component through the live
    // diff; no Bootstrap dropdown JS is involved.
    private bool _open;

    protected override RenderResult Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var bound = Bind is not null;
        if (bound == Value is not null)
        {
            throw new InvalidOperationException(
                "MultiSelect requires exactly one of Bind (bound mode) or Value (controlled mode).");
        }

        var comparer = EqualityComparer<TItem>.Default;
        var disabled = Disabled == true; // when disabled the whole control is inert: no wired handlers

        // Resolve the binding once per render. In controlled mode there is no EditContext/field; the
        // selection is read straight from Value and changes flow out through OnChange.
        ExpressionAccessor.Accessor? acc = null;
        EditContext? ctx = null;
        var fid = default(FieldIdentifier);
        ICollection<TItem>? selected;
        if (bound)
        {
            acc = ExpressionAccessor.Parse(Bind!);
            ctx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            // Always register — null clears a stale rule from a prior render, matching Input's BoundCore.
            // Collapse the typed sync/async validators into the single Delegate the context dispatches.
            ctx?.RegisterFieldValidator(fid, (Delegate?)Validate ?? ValidateAsync, () => acc.Getter());
            selected = acc.Getter() as ICollection<TItem>;
        }
        else
        {
            selected = Value;
        }

        Child LabelOf(TItem item) =>
            OptionLabel is not null ? OptionLabel(item) : item?.ToString() ?? string.Empty;

        // The control box doubles as the dropdown toggle. It's a <div> (not a <button>) so the per-chip
        // remove buttons nest as valid HTML; the client dispatches to the nearest data-rask-on-click, so
        // clicking a chip's × fires only its own handler, not the box toggle.
        var box = new List<Child>();
        if (selected is null || selected.Count == 0)
        {
            box.Add(Span(Class: "text-secondary")[Placeholder ?? "Select…"]);
        }
        else
        {
            var i = 0;
            foreach (var item in selected)
            {
                var captured = item;
                box.Add(Span(Class: "badge text-bg-primary d-inline-flex align-items-center", Key: i)[
                    LabelOf(captured),
                    Button(
                        Type: "button",
                        Class: "btn-close btn-close-white ms-1",
                        Disabled: Disabled,
                        OnClickAsync: disabled ? null : () => ToggleAsync(acc, ctx, fid, captured, comparer, add: false))
                ]);
                i++;
            }
        }

        // One row per option; a (display-only) checkbox shows membership and the row toggles it. The menu
        // stays open across selections — only the box / backdrop / Esc toggle _open.
        var rows = new List<Child>();
        var idx = 0;
        foreach (var option in Options)
        {
            var captured = option;
            var isChecked = selected is not null && selected.Contains(captured, comparer);
            rows.Add(Button(
                Type: "button",
                Class: "dropdown-item d-flex align-items-center gap-2",
                Disabled: Disabled,
                OnClickAsync: disabled ? null : () => ToggleAsync(acc, ctx, fid, captured, comparer, add: !isChecked),
                Key: idx)[
                Input("checkbox", Class: "form-check-input m-0 pe-none", Checked: isChecked),
                LabelOf(captured)
            ]);
            idx++;
        }

        var children = new List<Child>
        {
            // When disabled, the toggle drops its click handler and gains Bootstrap's .disabled look —
            // mirroring the disabled option/chip buttons so the whole control is inert. TabIndex makes the
            // box focusable so Esc (OnKeyDown) reaches it once the user has opened the menu.
            Div(
                Class: disabled
                    ? "form-select h-auto d-flex flex-wrap align-items-center gap-1 disabled pe-none"
                    : "form-select h-auto d-flex flex-wrap align-items-center gap-1",
                TabIndex: disabled ? null : 0,
                OnClick: disabled ? null : () => _open = !_open,
                OnKeyDown: disabled ? null : OnBoxKeyDown)[box],
            Div(Class: _open ? "dropdown-menu show d-block w-100" : "dropdown-menu")[rows]
        };

        // Click-outside: a transparent full-viewport backdrop sits behind the open menu but above the rest
        // of the page; any click that misses the menu lands here and closes. Its z-index is below the
        // Bootstrap dropdown-menu (1000) so option clicks reach the menu, not the backdrop.
        if (_open && !disabled)
        {
            children.Add(Div(
                Class: "position-fixed top-0 start-0 w-100 h-100",
                Style: "z-index: 999;",
                OnClick: () => _open = false));
        }

        if (bound)
        {
            children.Add(ValidationMessage(Bind!, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]]));
        }

        return Div(Class: "dropdown", Id: Id)[children];
    }

    private void OnBoxKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _open = false;
        }
    }

    // Adds/removes the item by comparer equality, then (bound) notifies + revalidates the field and runs
    // AfterBind, or (controlled) emits a fresh collection through OnChange (auto-wrapped, so the consumer
    // re-renders).
    private async Task ToggleAsync(
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        FieldIdentifier fid,
        TItem item,
        IEqualityComparer<TItem> comparer,
        bool add)
    {
        if (acc is not null)
        {
            await ToggleBoundAsync(acc, ctx, fid, item, comparer, add).ConfigureAwait(false);
        }
        else
        {
            await ToggleControlledAsync(item, comparer, add).ConfigureAwait(false);
        }
    }

    private async Task ToggleBoundAsync(
        ExpressionAccessor.Accessor acc,
        EditContext? ctx,
        FieldIdentifier fid,
        TItem item,
        IEqualityComparer<TItem> comparer,
        bool add)
    {
        // Re-resolve from the accessor (the model may have swapped the collection instance).
        if (acc.Getter() is not ICollection<TItem> collection)
        {
            return;
        }

        BindingHelpers.SetCollectionMembership(collection, item, add, comparer);
        await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);

        AfterBind?.Invoke(collection);
        if (AfterBindAsync is not null)
        {
            await AfterBindAsync(collection).ConfigureAwait(false);
        }
    }

    private async Task ToggleControlledAsync(TItem item, IEqualityComparer<TItem> comparer, bool add)
    {
        // The parent owns Value; never mutate it in place. Build the next selection and hand it back.
        var next = Value is null ? new List<TItem>() : new List<TItem>(Value);
        BindingHelpers.SetCollectionMembership(next, item, add, comparer);

        OnChange?.Invoke(next);
        if (OnChangeAsync is not null)
        {
            await OnChangeAsync(next).ConfigureAwait(false);
        }
    }
}
