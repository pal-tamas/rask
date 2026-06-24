using System.Linq.Expressions;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Example.Shared;

// Bind-first entry points for MultiSelect<TItem>, the bound-mode counterpart to the generated controlled
// factory (which takes Options first). Mirrors Input.Bound: a [GenerateForwarderFactory] forwarder can't be
// used here because the component is generic (a static forwarder method can't reuse the class type
// parameter), so these are hand-written overloads on the same `Generated` partial. Each forwards the shared
// presentation args to the controlled factory, then sets the [SkipFactory] bound-mode props on the returned
// (context-managed) instance. Validate fans into three overloads — none / sync / async — so the call site
// passes a typed lambda without a cast, exactly like Input's three Bound overloads.
public static partial class Generated
{
    public static MultiSelect<TItem> MultiSelect<TItem>(
        Expression<Func<ICollection<TItem>>> Bind,
        IEnumerable<TItem> Options,
        Func<TItem, Child>? OptionLabel = null,
        Action<ICollection<TItem>>? AfterBind = null,
        Func<ICollection<TItem>, Task>? AfterBindAsync = null,
        string? Id = null,
        string? Placeholder = null,
        bool? Disabled = null)
        => BindCore(Bind, Options, null, OptionLabel, AfterBind, AfterBindAsync, Id, Placeholder, Disabled);

    public static MultiSelect<TItem> MultiSelect<TItem>(
        Expression<Func<ICollection<TItem>>> Bind,
        IEnumerable<TItem> Options,
        Validate<ICollection<TItem>> Validate,
        Func<TItem, Child>? OptionLabel = null,
        Action<ICollection<TItem>>? AfterBind = null,
        Func<ICollection<TItem>, Task>? AfterBindAsync = null,
        string? Id = null,
        string? Placeholder = null,
        bool? Disabled = null)
        => BindCore(Bind, Options, Validate, OptionLabel, AfterBind, AfterBindAsync, Id, Placeholder, Disabled);

    public static MultiSelect<TItem> MultiSelect<TItem>(
        Expression<Func<ICollection<TItem>>> Bind,
        IEnumerable<TItem> Options,
        ValidateAsync<ICollection<TItem>> Validate,
        Func<TItem, Child>? OptionLabel = null,
        Action<ICollection<TItem>>? AfterBind = null,
        Func<ICollection<TItem>, Task>? AfterBindAsync = null,
        string? Id = null,
        string? Placeholder = null,
        bool? Disabled = null)
        => BindCore(Bind, Options, Validate, OptionLabel, AfterBind, AfterBindAsync, Id, Placeholder, Disabled);

    private static MultiSelect<TItem> BindCore<TItem>(
        Expression<Func<ICollection<TItem>>> bind,
        IEnumerable<TItem> options,
        Delegate? validate,
        Func<TItem, Child>? optionLabel,
        Action<ICollection<TItem>>? afterBind,
        Func<ICollection<TItem>, Task>? afterBindAsync,
        string? id,
        string? placeholder,
        bool? disabled)
    {
        // Build through the generated controlled factory so the instance is context-managed (RASK014),
        // then layer on the [SkipFactory] bound-mode props. This runs every render (it is the call site),
        // so the props are re-applied each frame just like generated factory params.
        var c = MultiSelect<TItem>(options, OptionLabel: optionLabel, Id: id, Placeholder: placeholder, Disabled: disabled);
        c.Bind = bind;
        c.AfterBind = afterBind;
        c.AfterBindAsync = afterBindAsync;
        c.Validate = validate;
        return c;
    }
}
