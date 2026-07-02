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

    // View state only — the selection lives in the bound model / parent Value. Toggling re-renders.
    private bool _open;

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

        Component LabelOf(TItem item) =>
            OptionLabel is not null ? OptionLabel(item) : item?.ToString() ?? string.Empty;

        // The control box doubles as the dropdown toggle; chips reuse BsBadge + BsCloseButton.
        var box = new List<Component>();
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
                box.Add(BsBadge(Color: BsColor.Primary, Class: "d-inline-flex align-items-center", Key: i)[
                    LabelOf(captured),
                    BsCloseButton(White: true, Class: "ms-1", Disabled: Disabled,
                        OnClickAsync: disabled ? null : () => ToggleAsync(acc, ctx, fid, captured, comparer, add: false))
                ]);
                i++;
            }
        }

        var rows = new List<Component>();
        var idx = 0;
        foreach (var option in Options)
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

        var children = new List<Component>
        {
            Div(
                Class: disabled
                    ? "form-select h-auto d-flex flex-wrap align-items-center gap-1 disabled pe-none"
                    : "form-select h-auto d-flex flex-wrap align-items-center gap-1",
                TabIndex: disabled ? null : 0,
                OnClick: disabled ? null : () => _open = !_open,
                OnKeyDown: disabled ? null : OnBoxKeyDown)[box],
            Div(Class: _open ? "dropdown-menu show d-block w-100" : "dropdown-menu")[rows]
        };

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

        return Div(Class: BsClass.Join("dropdown", Class), Id: Id)[children];
    }

    private void OnBoxKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _open = false;
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
