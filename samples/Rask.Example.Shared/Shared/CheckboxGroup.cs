using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Example.Shared;

// Example generic form control: a set of Bootstrap 5.3 checkboxes
// (https://getbootstrap.com/docs/5.3/forms/checks-radios/) selecting many values into an ICollection<TItem>.
// Structured like MultiSelect<TItem> — a Component with two usage shapes:
//   • Bound      — CheckboxGroup<string>(() => model.Tags, options, Validate: …) two-way binds the model
//                  collection, runs the per-field Validate rule, and surfaces it via the embedded
//                  ValidationMessage. AfterBind/AfterBindAsync are post-bind hooks (the bound value passed in).
//   • Controlled — CheckboxGroup<string>(options, Value: selection, OnChange: next => …) the parent owns the
//                  collection; OnChange/OnChangeAsync (auto-wrapped) deliver the new selection and re-render
//                  the host. No EditContext, so no Validate in this mode.
// Each item is a <div class="form-check"> wrapping a .form-check-input + .form-check-label tied by id/for;
// ItemClass adds extra wrapper classes (e.g. "form-check-inline").
public sealed class CheckboxGroup<TItem> : Component
{
    public required IEnumerable<TItem> Options { get; set; }

    // Controlled mode (no Bind).
    public ICollection<TItem>? Value { get; set; }
    public Action<IReadOnlyCollection<TItem>>? OnChange { get; set; }
    public Func<IReadOnlyCollection<TItem>, Task>? OnChangeAsync { get; set; }

    // Bound mode — set through the Bind-first factory overloads, kept off the controlled factory.
    [SkipFactory] public Expression<Func<ICollection<TItem>>>? Bind { get; set; }
    [SkipFactory] public Action<ICollection<TItem>>? AfterBind { get; set; }
    [SkipFactory] public Func<ICollection<TItem>, Task>? AfterBindAsync { get; set; }
    [SkipFactory] public Delegate? Validate { get; set; }

    public Func<TItem, Child>? OptionLabel { get; set; }
    public string? Name { get; set; }
    public string? ItemClass { get; set; }
    public bool? Disabled { get; set; }

    // Bound-mode entry — the generator fans this into none/sync/async Validate flavors (Validate over the
    // ICollection<TItem> from the Bind expression), each forwarding here; builds via the controlled factory
    // (RASK014) and layers on the [SkipFactory] bound-mode props.
    [GenerateForwarderFactory(Validator = "Validate")]
    public static CheckboxGroup<TItem> Bound(
        Expression<Func<ICollection<TItem>>> Bind,
        IEnumerable<TItem> Options,
        Delegate? Validate = null,
        Action<ICollection<TItem>>? AfterBind = null,
        Func<ICollection<TItem>, Task>? AfterBindAsync = null,
        Func<TItem, Child>? OptionLabel = null,
        string? Name = null,
        string? ItemClass = null,
        bool Disabled = false)
    {
        var c = Generated.CheckboxGroup<TItem>(
            Options, OptionLabel: OptionLabel, Name: Name, ItemClass: ItemClass, Disabled: Disabled);
        c.Bind = Bind;
        c.AfterBind = AfterBind;
        c.AfterBindAsync = AfterBindAsync;
        c.Validate = Validate;
        return c;
    }

    protected override RenderResult Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var bound = Bind is not null;
        if (bound == Value is not null)
        {
            throw new InvalidOperationException(
                "CheckboxGroup requires exactly one of Bind (bound mode) or Value (controlled mode).");
        }

        var comparer = EqualityComparer<TItem>.Default;
        ExpressionAccessor.Accessor? acc = null;
        EditContext? ctx = null;
        var fid = default(FieldIdentifier);
        ICollection<TItem>? selected;
        if (bound)
        {
            acc = ExpressionAccessor.Parse(Bind!);
            ctx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            ctx?.RegisterFieldValidator(fid, Validate, () => acc.Getter());
            selected = acc.Getter() as ICollection<TItem>;
        }
        else
        {
            selected = Value;
        }

        var disabled = Disabled == true;
        var groupName = Name ?? acc?.PropertyName ?? "checkbox-group";
        var wrapperClass = ItemClass is null ? "form-check" : $"form-check {ItemClass}";

        var children = new List<Child>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option;
            var optionId = $"{groupName}-{index}";
            var isChecked = selected is not null && selected.Contains(optionValue, comparer);
            Child label = OptionLabel is not null ? OptionLabel(option) : option?.ToString() ?? string.Empty;

            children.Add(Div(Class: wrapperClass, Key: index)[
                Input(
                    "checkbox",
                    groupName,
                    BindingHelpers.FormatValue(option),
                    Checked: isChecked,
                    Disabled: Disabled,
                    Class: "form-check-input",
                    Id: optionId,
                    // The checkbox change payload carries the new checked state as a bool string.
                    OnChangeAsync: disabled
                        ? null
                        : value => ToggleAsync(acc, ctx, fid, optionValue, comparer, bool.TryParse(value, out var b) && b)),
                Label(Class: "form-check-label", For: optionId)[label]
            ]);
            index++;
        }

        if (bound)
        {
            children.Add(ValidationMessage(Bind!, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]]));
        }

        return Fragment()[children];
    }

    private async Task ToggleAsync(
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx,
        FieldIdentifier fid,
        TItem item,
        IEqualityComparer<TItem> comparer,
        bool include)
    {
        if (acc is not null)
        {
            if (acc.Getter() is not ICollection<TItem> collection)
            {
                return;
            }

            BindingHelpers.SetCollectionMembership(collection, item, include, comparer);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            AfterBind?.Invoke(collection);
            if (AfterBindAsync is not null)
            {
                await AfterBindAsync(collection).ConfigureAwait(false);
            }
        }
        else
        {
            var next = Value is null ? new List<TItem>() : new List<TItem>(Value);
            BindingHelpers.SetCollectionMembership(next, item, include, comparer);
            OnChange?.Invoke(next);
            if (OnChangeAsync is not null)
            {
                await OnChangeAsync(next).ConfigureAwait(false);
            }
        }
    }
}
