using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Example.Shared;

// Example generic form control (the single-value sibling of CheckboxGroup): binds a single TValue model
// property to a set of mutually-exclusive radios. Parses the expression, resolves the ambient EditContext,
// and wires each radio's change handler to set the property and re-validate. Emits Bootstrap 5.3 check
// markup (https://getbootstrap.com/docs/5.3/forms/checks-radios/): each item is a <div class="form-check">
// wrapping a .form-check-input radio and a .form-check-label tied together by id/for. ItemClass adds extra
// classes to that wrapper (e.g. "form-check-inline"). Returns a transparent Fragment so the consumer owns
// the surrounding layout.
public static partial class Generated
{
    public static Component RadioGroup<TValue>(
        Expression<Func<TValue>> Bind,
        IEnumerable<TValue> Options,
        Func<TValue, Child>? OptionLabel = null,
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
        var current = acc.Getter();
        var comparer = EqualityComparer<TValue>.Default;
        var wrapperClass = ItemClass is null ? "form-check" : $"form-check {ItemClass}";

        var children = new List<Child>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option; // capture per iteration for the handler closure
            var optionId = $"{groupName}-{index}";
            var isChecked = current is TValue typed && comparer.Equals(option, typed);
            Child label = OptionLabel is not null ? OptionLabel(option) : option?.ToString() ?? string.Empty;

            children.Add(Div(Class: wrapperClass, Key: index)[
                Input(
                    "radio",
                    groupName,
                    BindingHelpers.FormatValue(option),
                    Checked: isChecked,
                    Disabled: Disabled,
                    Class: "form-check-input",
                    Id: optionId,
                    OnChangeAsync: async _ =>
                    {
                        // A radio only fires change when it becomes selected → set the bound value.
                        acc.Setter(optionValue);
                        ctx?.NotifyFieldChanged(fid);
                        ctx?.NotifyFieldTouched(fid);
                        if (ctx is not null)
                        {
                            await ctx.ValidateFieldAsync(fid).ConfigureAwait(false);
                        }
                    }),
                Label(Class: "form-check-label", For: optionId)[label]
            ]);
            index++;
        }

        return Fragment()[children];
    }
}
