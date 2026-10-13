using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// The two halves of <c>IFormControl&lt;T&gt;</c> plumbing that a control drawing its own markup has to
/// write out by hand: resolve the binding at the top of a render, commit a value at the bottom.
/// </summary>
/// <remarks>
/// <para>
/// The controls that wrap a single <c>&lt;input&gt;</c> — <see cref="UiInput{T}" />,
/// <see cref="UiTextarea{T}" />, <see cref="UiCheckbox" />, <see cref="UiSwitch" /> — need none of this: <c>Rask.Core</c>'s own
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

        // What the reader chose and the session still holds is what the control draws (PendingBinds).
        var shown = control is Component drawn && PendingBinds.TryPeek(drawn, accessor.Field, out var chosen)
            ? chosen
            : accessor.Getter();

        return (accessor, context, shown is T value ? value : default);
    }

    /// <summary>
    /// Commits a new value — to the parent's <c>OnChange</c> when controlled, to the model and the
    /// <c>EditContext</c> when bound. When a bound value is written is <c>Rask.Core</c>'s to say
    /// (<see cref="PendingBinds" />): with the next action, or at once for a control that says <c>.Live()</c>.
    /// </summary>
    /// <remarks>
    /// Every commit in this class is the reader's doing, and says so (<see cref="ReaderEdits" />): a form that
    /// asks before it is left counts it, written yet or not. A value the page gives a control goes through
    /// none of them.
    /// </remarks>
    internal static Task CommitAsync<T>(
        IFormControl<T> control,
        ExpressionAccessor.Accessor? accessor,
        EditContext? context,
        T value)
    {
        ReaderEdits.Note((Component)control);
        if (accessor is null)
        {
            return control.InvokeOnChange(value);
        }

        return PendingBinds.Commit((Component)control, accessor.Field, context, value, Write, SettleAsync);

        bool Write()
        {
            accessor.Setter(value);
            return true;
        }

        async Task SettleAsync()
        {
            await BindingHelpers.NotifyAndValidateField(context, accessor.Field).ConfigureAwait(false);
            await control.InvokeAfterBind(value).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Commits "nothing chosen" for a control typed into a part at a time whose parts were all emptied:
    /// <see langword="null" /> where the bound member can say so, the type's unset value where it cannot.
    /// </summary>
    internal static Task ClearAsync<T>(IFormControl<T> control, ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        ReaderEdits.Note((Component)control);
        if (accessor is null)
        {
            return control.InvokeOnChange(default!);
        }

        return PendingBinds.Commit((Component)control, accessor.Field, context, default(T), Write, SettleAsync);

        bool Write()
        {
            accessor.Setter(Nullable.GetUnderlyingType(accessor.PropertyType) is null ? default(T) : null);
            return true;
        }

        async Task SettleAsync()
        {
            await BindingHelpers.NotifyAndValidateField(context, accessor.Field).ConfigureAwait(false);
            await control.InvokeAfterBind(default!).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes what a control's parts hold when it is no value — a day and no month, an hour and no minute. The
    /// model keeps what it held, and the form says <paramref name="sentence" /> under the field until a value
    /// arrives (<c>EditContext.NoteUnread</c>): with the next action, like a value, or at once for a control
    /// that says <c>.Live()</c>. A controlled control's parent hears nothing, as it hears of no value.
    /// </summary>
    internal static Task UnreadAsync<T>(
        IFormControl<T> control, ExpressionAccessor.Accessor? accessor, EditContext? context, T? shown, RaskString sentence)
    {
        ReaderEdits.Note((Component)control);
        if (accessor is null || context is null)
        {
            return Task.CompletedTask;
        }

        return PendingBinds.Commit((Component)control, accessor.Field, context, shown, Note, static () => Task.CompletedTask);

        bool Note()
        {
            context.NoteUnread(accessor.Field, sentence);
            return false;
        }
    }

    /// <summary>
    /// What a group of typed parts carries while a message shows under its field: the mark that asks to hear
    /// the first key of a correction (<c>data-rask-on-edit</c>), which takes the message away as it does under
    /// an input. Nothing while the field is valid.
    /// </summary>
    internal static (string Name, string? Value)[] FirstEdit(ExpressionAccessor.Accessor? accessor, EditContext? context)
    {
        if (accessor is null || context is null || !context.IsInvalid(accessor.Field)
            || Rask.Core.Live.LiveRenderContext.CurrentSync is not { } live)
        {
            return [];
        }

        var field = accessor.Field;
        return [("data-rask-on-edit", live.RegisterHandler(() => context.ClearMessages(field)))];
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
    /// <c>Select&lt;T&gt;</c> cannot, and why <c>src/Rask.Site</c> still publishes with zero trim warnings.
    /// </para>
    /// </remarks>
    internal static async Task CommitSelectionAsync<T>(
        IFormControl<ICollection<T>> control,
        ExpressionAccessor.Accessor? accessor,
        EditContext? context,
        IReadOnlyList<T> picked)
    {
        ReaderEdits.Note((Component)control);
        if (accessor is null)
        {
            // Controlled: a fresh list, never the collection the parent handed down. Mutating that one
            // would change the parent's state behind its back and leave OnChange looking like a no-op,
            // because the "new" value and the old are the same object.
            await control.InvokeOnChange([.. picked]).ConfigureAwait(false);
            return;
        }

        // A copy: what waits is what was picked now, whatever the caller does with its list afterwards.
        List<T> chosen = [.. picked];
        await PendingBinds.Commit((Component)control, accessor.Field, context, chosen, Write, SettleAsync)
            .ConfigureAwait(false);

        bool Write() => TryWriteSelection(accessor, chosen);

        async Task SettleAsync()
        {
            await BindingHelpers.NotifyAndValidateField(context, accessor.Field).ConfigureAwait(false);
            await control.InvokeAfterBind(accessor.Getter() as ICollection<T> ?? chosen).ConfigureAwait(false);
        }
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
