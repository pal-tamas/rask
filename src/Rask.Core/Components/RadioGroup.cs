using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

// Hand-written generic factory (same shape as VirtualizeModel<T> / Context.Provide<T>): binds a single
// TValue model property to a set of mutually-exclusive radio inputs. Mirrors Input.Bound — parses
// the expression, resolves the ambient EditContext, and wires each radio's change handler to set
// the property and re-validate. Returns a transparent Fragment of <label><input radio>…</label>
// so the consumer controls layout via Class/ItemClass.
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

        var children = new List<Child>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option; // capture per iteration for the handler closure
            var isChecked = current is TValue typed && comparer.Equals(option, typed);
            var label = OptionLabel is not null ? OptionLabel(option) : option?.ToString() ?? string.Empty;

            children.Add(Label(Class: ItemClass)[
                Input(
                    "radio",
                    groupName,
                    BindingHelpers.FormatValue(option),
                    Checked: isChecked,
                    Disabled: Disabled,
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
                    },
                    Key: index),
                label
            ]);
            index++;
        }

        return new Fragment(children);
    }
}
