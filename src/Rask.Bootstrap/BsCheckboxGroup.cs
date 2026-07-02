using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A set of Bootstrap checkboxes selecting many values into an ICollection<TItem>. Implements
// IFormControl<ICollection<TItem>> for both the bound (BsCheckboxGroup(() => model.Tags, options)) and
// controlled (Value:/OnChange:) shapes. Each item is a <div class="form-check"> with a .form-check-input
// + .form-check-label; the embedded ValidationMessage surfaces the per-field rule.
public sealed class BsCheckboxGroup<TItem> : Component, IFormControl<ICollection<TItem>>
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
    public string? Name { get; set; }
    public string? ItemClass { get; set; }
    public bool? Disabled { get; set; }

    protected override Component? Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var bound = Bind is not null;
        if (bound == Value is not null)
        {
            throw new InvalidOperationException(
                "BsCheckboxGroup requires exactly one of Bind (bound mode) or Value (controlled mode).");
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
            ((IFormControl<ICollection<TItem>>)this).RegisterValidator(acc, ctx);
            selected = acc.Getter() as ICollection<TItem>;
        }
        else
        {
            selected = Value;
        }

        var disabled = Disabled == true;
        var groupName = Name ?? acc?.PropertyName ?? "checkbox-group";
        var wrapperClass = BsClass.Join("form-check", ItemClass);

        var children = new List<Component>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option;
            var optionId = $"{groupName}-{index}";
            var isChecked = selected is not null && selected.Contains(optionValue, comparer);
            Component label = OptionLabel is not null ? OptionLabel(option) : option?.ToString() ?? string.Empty;

            children.Add(Div(Class: wrapperClass, Key: index)[
                Input<string>(
                    InputType.Checkbox, groupName, BindingHelpers.FormatValue(option),
                    Checked: isChecked, Disabled: Disabled, Class: "form-check-input", Id: optionId,
                    OnChangeAsync: disabled
                        ? null
                        : value => ToggleAsync(acc, ctx, fid, optionValue, comparer, bool.TryParse(value, out var b) && b)),
                Rask.Core.Components.Generated.Label(Class: "form-check-label", For: optionId)[label]
            ]);
            index++;
        }

        if (bound)
        {
            children.Add(ValidationMessage(Bind!, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]]));
        }

        return [.. children];
    }

    private async Task ToggleAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TItem item, IEqualityComparer<TItem> comparer, bool include)
    {
        if (acc is not null)
        {
            if (acc.Getter() is not ICollection<TItem> collection)
            {
                return;
            }

            BindingHelpers.SetCollectionMembership(collection, item, include, comparer);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            await ((IFormControl<ICollection<TItem>>)this).InvokeAfterBindAsync(collection).ConfigureAwait(false);
        }
        else
        {
            var next = Value is null ? new List<TItem>() : new List<TItem>(Value);
            BindingHelpers.SetCollectionMembership(next, item, include, comparer);
            await ((IFormControl<ICollection<TItem>>)this).InvokeOnChangeAsync(next).ConfigureAwait(false);
        }
    }
}
