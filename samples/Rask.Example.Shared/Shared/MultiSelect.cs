using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Example.Shared;

// Generic multiselect: a custom Bootstrap dropdown of checkable options with the chosen items shown as
// removable chips. Unlike the native <select multiple>, it binds to an ICollection<TItem> model property
// and drives the ambient EditContext (validation) — the same contract as CheckboxGroup<TItem>. Open/close
// is pure server live-diff state (no client JS; the showcase loads Bootstrap CSS only). It demonstrates
// the public binding API: ExpressionAccessor.Parse + BindingHelpers.ResolveBindingContext are all a
// custom form control needs to bind to a model and report changes/validation.
//
//   MultiSelect<string>(() => model.Interests, new[] { "Web", "Mobile", "AI" })
//
// The OnChange callback re-renders the consumer (a component can't re-render its own parent), so the
// parent's summary/validation updates as selections change.
public sealed class MultiSelect<TItem> : Component
{
    public required Expression<Func<ICollection<TItem>>> Bind { get; set; }
    public required IEnumerable<TItem> Options { get; set; }
    public Func<TItem, Child>? OptionLabel { get; set; }
    public Action? OnChange { get; set; }
    public string? Id { get; set; }
    public string? Placeholder { get; set; }
    public bool? Disabled { get; set; }

    // View state only — the selection itself lives in the bound model collection. Toggling re-renders
    // this component through the live diff; no Bootstrap dropdown JS is involved.
    private bool _open;

    protected override RenderResult Render()
    {
        ArgumentNullException.ThrowIfNull(Bind);
        ArgumentNullException.ThrowIfNull(Options);

        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var selected = acc.Getter() as ICollection<TItem>;
        var comparer = EqualityComparer<TItem>.Default;
        var disabled = Disabled == true; // when disabled the whole control is inert: no wired handlers

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
        // stays open across selections — only the box toggles _open.
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

        return Div(Class: "dropdown", Id: Id)[
            // When disabled, the toggle drops its click handler and gains Bootstrap's .disabled look —
            // mirroring the disabled option/chip buttons so the whole control is inert.
            Div(
                Class: disabled
                    ? "form-select h-auto d-flex flex-wrap align-items-center gap-1 disabled pe-none"
                    : "form-select h-auto d-flex flex-wrap align-items-center gap-1",
                OnClick: disabled ? null : () => _open = !_open)[box],
            Div(Class: _open ? "dropdown-menu show d-block w-100" : "dropdown-menu")[rows],
            ValidationMessage(Bind, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]])
        ];
    }

    // Re-resolves the collection from the accessor (the model may have swapped it), adds/removes the item
    // by comparer equality, then notifies + revalidates the field and pings the consumer to re-render.
    private async Task ToggleAsync(
        ExpressionAccessor.Accessor acc,
        EditContext? ctx,
        FieldIdentifier fid,
        TItem item,
        IEqualityComparer<TItem> comparer,
        bool add)
    {
        if (acc.Getter() is not ICollection<TItem> collection)
        {
            return;
        }

        if (add)
        {
            if (!collection.Contains(item, comparer))
            {
                collection.Add(item);
            }
        }
        else
        {
            Remove(collection, item, comparer);
        }

        ctx?.NotifyFieldChanged(fid);
        ctx?.NotifyFieldTouched(fid);
        if (ctx is not null)
        {
            await ctx.ValidateFieldAsync(fid).ConfigureAwait(false);
        }

        OnChange?.Invoke();
    }

    // Membership uses Enumerable.Contains(comparer); removal finds the match first and removes it after
    // the loop (never mutating mid-enumeration), matching CheckboxGroup's vetted pattern. ICollection.Remove
    // uses the collection's default equality, which can differ from the supplied comparer, so we remove the
    // exact captured instance.
    private static void Remove(ICollection<TItem> collection, TItem item, IEqualityComparer<TItem> comparer)
    {
        TItem? match = default;
        var found = false;
        foreach (var existing in collection)
        {
            if (comparer.Equals(existing, item))
            {
                match = existing;
                found = true;
                break;
            }
        }

        if (found)
        {
            collection.Remove(match!);
        }
    }
}
