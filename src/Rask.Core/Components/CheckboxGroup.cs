using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

// Hand-written generic factory: binds an ICollection<TItem> model property to a set of checkboxes,
// one per option. Toggling an option adds/removes it from the bound collection (mutated in place)
// and re-validates the field. Returns a transparent Fragment so the consumer owns layout.
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

        var children = new List<Child>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option; // capture per iteration
            var isChecked = selected is not null && Contains(selected, optionValue, comparer);
            Child label = OptionLabel is not null ? OptionLabel(option) : option?.ToString() ?? string.Empty;

            children.Add(Label(Class: ItemClass)[
                Input(
                    Type: "checkbox",
                    Name: groupName,
                    Value: BindingHelpers.FormatValue(option),
                    Checked: isChecked,
                    Disabled: Disabled,
                    // The checkbox change payload carries the new checked state as a bool string.
                    OnChangeAsync: async value =>
                    {
                        if (acc.Getter() is not ICollection<TItem> collection)
                        {
                            return;
                        }

                        var nowChecked = bool.TryParse(value, out var b) && b;
                        if (nowChecked)
                        {
                            if (!Contains(collection, optionValue, comparer))
                            {
                                collection.Add(optionValue);
                            }
                        }
                        else
                        {
                            Remove(collection, optionValue, comparer);
                        }

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

    private static bool Contains<TItem>(ICollection<TItem> collection, TItem item, IEqualityComparer<TItem> comparer)
    {
        foreach (var existing in collection)
        {
            if (comparer.Equals(existing, item))
            {
                return true;
            }
        }

        return false;
    }

    private static void Remove<TItem>(ICollection<TItem> collection, TItem item, IEqualityComparer<TItem> comparer)
    {
        // Remove by comparer equality (collection.Remove uses default equality, which for records
        // is value equality but for reference items may differ from the supplied comparer).
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
