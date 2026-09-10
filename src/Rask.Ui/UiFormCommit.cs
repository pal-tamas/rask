using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// The two halves of <c>IFormControl&lt;T&gt;</c> plumbing that a control drawing its own markup has to
/// write out by hand: resolve the binding at the top of a render, commit a value at the bottom.
/// </summary>
/// <remarks>
/// <para>
/// The controls that wrap a single <c>&lt;input&gt;</c> — <see cref="UiInput{T}" />,
/// <see cref="UiTextarea{T}" />, <see cref="UiCheckbox" /> — need none of this: <c>Rask.Core</c>'s own
/// <c>Input&lt;T&gt;</c> is an <c>IFormControl&lt;T&gt;</c>, so they forward <c>Bind</c> and the
/// framework does the rest. It is the ones built from several elements — a radio group, a grid of
/// buttons — that have to drive the binding themselves, and there are five of them, which is four too
/// many to repeat it in.
/// </para>
/// <para>
/// It follows <c>docs/building-form-controls.md</c> §2 exactly; the point here is that it follows it in
/// one place.
/// </para>
/// </remarks>
internal static class UiFormCommit
{
    /// <summary>
    /// Resolves the binding for one render and returns the value to draw, from the model when bound and
    /// from <c>Value</c> when controlled.
    /// </summary>
    internal static (ExpressionAccessor.Accessor? Accessor, EditContext? Context, T? Current) Resolve<T>(
        IFormControl<T> control)
    {
        if (control.Bind is not { } bind)
        {
            return (null, null, control.Value);
        }

        var accessor = ExpressionAccessor.Parse(bind);
        var context = BindingHelpers.ResolveBindingContext(accessor.Target);

        // Every render, deliberately: re-registering the collapsed validator is also what clears a stale
        // rule when the consumer stops supplying one.
        control.RegisterValidator(accessor, context);

        return (accessor, context, accessor.Getter() is T value ? value : default);
    }

    /// <summary>
    /// Writes a new value back — to the model and the <c>EditContext</c> when bound, to the parent's
    /// <c>OnChange</c> when controlled.
    /// </summary>
    internal static async Task CommitAsync<T>(
        IFormControl<T> control,
        ExpressionAccessor.Accessor? accessor,
        EditContext? context,
        T value)
    {
        if (accessor is not null)
        {
            accessor.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(context, accessor.Field).ConfigureAwait(false);
            await control.InvokeAfterBindAsync(value).ConfigureAwait(false);
        }
        else
        {
            await control.InvokeOnChangeAsync(value).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a whole selection back for a control whose value is a collection, into whatever shape the
    /// bound property declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CommitAsync{T}" /> cannot do this job. It ends in <c>accessor.Setter(value)</c>, and the
    /// ordinary way to declare one of these fields — <c>public List&lt;string&gt; Tags { get; } = [];</c> —
    /// has no setter at all. It also could not know which concrete collection to hand over: assigning a
    /// <c>List&lt;T&gt;</c> to a <c>T[]</c> property throws, and nothing in the value's own type says which
    /// it is.
    /// </para>
    /// <para>
    /// <b>Set, never toggle.</b> Every commit carries the ABSOLUTE selection rather than an add or a
    /// remove, which is what makes it self-correcting — a membership edit computed from a render-time
    /// snapshot cannot recover from a single missed frame, where a replace re-syncs on the next one. Same
    /// reasoning as <c>BindingHelpers.MultiSelectSetHandler</c>.
    /// </para>
    /// <para>
    /// <b>It is AOT-clean, and deliberately so.</b> Every collection built here is a generic instantiated
    /// over this method's own type parameter, so the compiler emits the code — there is no
    /// <c>MakeGenericType</c> and no <c>Array.CreateInstance</c>, the two calls that made
    /// <c>BindingHelpers.IsBindableSelectionType</c> close its element type to <c>string</c>. That is the
    /// whole reason a kit control can be generic over <typeparamref name="T" /> where the framework's own
    /// <c>Select&lt;T&gt;</c> cannot, and why <c>site/Rask.Site</c> still publishes with zero trim warnings.
    /// </para>
    /// </remarks>
    internal static async Task CommitSelectionAsync<T>(
        IFormControl<ICollection<T>> control,
        ExpressionAccessor.Accessor? accessor,
        EditContext? context,
        IReadOnlyList<T> picked)
    {
        if (accessor is null)
        {
            // Controlled: a fresh list, never the collection the parent handed down. Mutating that one
            // would change the parent's state behind its back and leave OnChange looking like a no-op,
            // because the "new" value and the old are the same object.
            await control.InvokeOnChangeAsync([.. picked]).ConfigureAwait(false);
            return;
        }

        if (!TryWriteSelection(accessor, picked))
        {
            return;
        }

        await BindingHelpers.NotifyAndValidateFieldAsync(context, accessor.Field).ConfigureAwait(false);
        await control.InvokeAfterBindAsync(accessor.Getter() as ICollection<T> ?? [.. picked])
            .ConfigureAwait(false);
    }

    // Returns false when the model's field cannot take the selection at all — a get-only property holding
    // a read-only collection — rather than throwing into a click handler.
    private static bool TryWriteSelection<T>(ExpressionAccessor.Accessor accessor, IReadOnlyList<T> picked)
    {
        // Checked FIRST, not as a fallback: assigning over a settable property is fine either way, while
        // calling a setter that does not exist is not. (BindingHelpers.TrySetSelection makes the same call
        // for the same reason.)
        if (accessor.Property.SetMethod is null)
        {
            if (accessor.Getter() is not ICollection<T> existing || existing.IsReadOnly)
            {
                return false;
            }

            existing.Clear();
            foreach (var item in picked)
            {
                existing.Add(item);
            }

            return true;
        }

        var declared = accessor.PropertyType;
        object value;
        if (declared == typeof(T[]))
        {
            value = picked.ToArray();
        }
        else if (declared == typeof(HashSet<T>) || declared == typeof(ISet<T>)
                                                || declared == typeof(IReadOnlySet<T>))
        {
            value = new HashSet<T>(picked);
        }
        else
        {
            // Everything else the chain can bind is satisfied by a list: List<T> itself, and the
            // ICollection<T>/IList<T>/IEnumerable<T>/IReadOnlyList<T> interfaces it implements.
            value = new List<T>(picked);
        }

        accessor.Setter(value);
        return true;
    }
}
