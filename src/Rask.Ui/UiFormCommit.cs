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
}
