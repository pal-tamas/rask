using System.Globalization;
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

    // The group's accessible name. When set, the checkboxes are wrapped in a <fieldset> named by a <legend>
    // (the correct grouping semantics + accessible name for a set of related checkboxes); when null, the bare
    // per-item fragment is kept so callers that supply their own fieldset/heading aren't double-wrapped.
    public string? Label { get; set; }
    public string? ItemClass { get; set; }
    public bool? Disabled { get; set; }

    // Unique suffix for the auto-generated group name of an UNNAMED group, so two id-less controlled
    // checkbox groups on one page don't both fall back to name="checkbox-group" and collide their ids.
    private readonly int _instanceId = BsInstanceId.Next();

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
        var groupName = Name ?? acc?.PropertyName
            ?? "checkbox-group-" + _instanceId.ToString(CultureInfo.InvariantCulture);
        var wrapperClass = BsClass.Join("form-check", ItemClass);

        // Reading GetValidationMessages here latches the render-cache opt-out (see BsFormControl) so the group
        // repaints its aria-invalid + feedback when a rule fails on submit, instead of serving a stale cache.
        // The error is a role="alert" live region carrying a stable id the boxes point at via aria-describedby.
        IReadOnlyList<string> messages = bound && ctx is not null ? ctx.GetValidationMessages(fid) : [];
        var invalid = messages.Count > 0;
        var errorId = invalid ? groupName + "-error" : null;
        var optionAria = BsClass.FieldAria(invalid, errorId);

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
                    Aria: optionAria,
                    OnChangeAsync: disabled
                        ? null
                        : value => ToggleAsync(acc, ctx, fid, optionValue, comparer, bool.TryParse(value, out var b) && b)),
                Rask.Core.Components.Generated.Label(Class: "form-check-label", For: optionId)[label]
            ]);
            index++;
        }

        if (invalid)
        {
            children.Add(Div(Id: errorId, Class: "invalid-feedback d-block", Role: "alert")[messages[0]]);
        }

        if (Label is not null)
        {
            var content = new List<Component> { Legend(Class: "form-label fs-6")[Label] };
            content.AddRange(children);
            // Disabled is NOT set on the fieldset: a disabled fieldset disables ALL descendants, which would
            // also disable interactive content in a rich OptionLabel. The checkboxes carry their own Disabled.
            return Fieldset(Class: "border-0 p-0 m-0")[content];
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
