using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Example.Shared;

// Example generic form control: binds an ICollection<TItem> model property to a set of checkboxes, one per
// option. Toggling an option adds/removes it from the bound collection (mutated in place) and re-validates
// the field. Emits Bootstrap 5.3 check markup (https://getbootstrap.com/docs/5.3/forms/checks-radios/):
// each item is a <div class="form-check"> wrapping a .form-check-input and a .form-check-label tied together
// by id/for. ItemClass adds extra classes to that wrapper (e.g. "form-check-inline"). Returns a transparent
// Fragment so the consumer owns the surrounding layout.
public static partial class Generated
{
    public static Component CheckboxGroup<TItem>(
        Expression<Func<ICollection<TItem>>> Bind,
        IEnumerable<TItem> Options,
        Func<TItem, Child>? OptionLabel = null,
        string? Name = null,
        string? ItemClass = null,
        bool Disabled = false)
    {
        ArgumentNullException.ThrowIfNull(Bind);
        ArgumentNullException.ThrowIfNull(Options);

        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var groupName = Name ?? acc.PropertyName;
        var selected = acc.Getter() as ICollection<TItem>;
        var comparer = EqualityComparer<TItem>.Default;
        var wrapperClass = ItemClass is null ? "form-check" : $"form-check {ItemClass}";

        var children = new List<Child>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option; // capture per iteration
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
                    OnChangeAsync: async value =>
                    {
                        if (acc.Getter() is not ICollection<TItem> collection)
                        {
                            return;
                        }

                        var nowChecked = bool.TryParse(value, out var b) && b;
                        BindingHelpers.SetCollectionMembership(collection, optionValue, nowChecked, comparer);
                        await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
                    }),
                Label(Class: "form-check-label", For: optionId)[label]
            ]);
            index++;
        }

        return Fragment()[children];
    }
}
