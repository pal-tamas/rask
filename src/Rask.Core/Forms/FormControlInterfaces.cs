using System.Linq.Expressions;

namespace Rask.Core.Forms;

// The contract a custom form control implements so the factory generator can synthesize its
// factories — both a bound factory (`Control(() => model.Field, …)`, two-way binding + per-field
// validation, with the validator fanned into none/sync/async overloads) and a controlled factory
// (`Control(…, Value: v, OnChange: …)`, parent-owned state). A control declares the value type T
// once; the generator reads it off the interface and emits the typed surface.
//
// Members use fixed names (Bind/Validate/…/Value/OnChange/…) — the generator recognizes them by
// name and excludes the bound-mode members from the controlled factory (so no [SkipFactory] is
// needed). `Validate<T>`/`ValidateAsync<T>` (this namespace) and `Action<T>`/`Func<T, Task>`
// (Rask.Core) are the framework's named delegate types; the generator collapses the sync/async
// validator pair into the none/sync/async factory fan-out, and auto-wraps OnChange/OnChangeAsync
// (AutoCallback) so invoking them re-renders the consumer.
//
// The framework's own component-style controls (samples MultiSelect/CheckboxGroup/RadioGroup) are
// the worked examples. In Render, collapse the typed validators for EditContext registration:
//   ctx?.RegisterFieldValidator(fid, (Delegate?)Validate ?? ValidateAsync, () => acc.Getter());
// Non-generic marker every IFormControl<T> carries, so the render machinery can recognise a form
// control without knowing its value type T (Component.GetOrCreateChild records the control's creating
// parent through it — see BindingConsumerRegistry). No members: it is purely a type tag.
public interface IFormControl;

public interface IFormControl<T> : IFormControl
{
    // Bound mode — two-way binds an lvalue of type T and drives the ambient EditContext.
    //
    // Ordinary delegates. They were briefly carriers — while a chain's receiver was the control itself,
    // `control.Validate(rule)` bound to the delegate-typed property and failed (CS1593) instead of
    // reaching the same-named setter. The receiver is `Build<TControl>` now, so nothing is in the way.
    /// <summary>
    ///     Two-way binds this control to a model property: <c>.Bind(() =&gt; _model.Email)</c>. Pass the
    ///     property itself, as a lambda — the expression is what lets the control read the current value,
    ///     write the new one back, and name the field to the surrounding <c>Form</c>'s validation.
    ///     <para>
    ///         Binding is one of the two ways to open a control, and it excludes the other: a bound control
    ///         cannot also be given a <see cref="Value" />, because that would be two sources of truth for
    ///         the same field. Choose <see cref="Bind" /> when the model owns the value and
    ///         <see cref="Value" /> when the parent does.
    ///     </para>
    /// </summary>
    Expression<Func<T>>? Bind { get; set; }

    /// <summary>
    ///     A validation rule for this field, run on change and on submit. Return an error message to reject
    ///     the value, or <see langword="null" /> to accept it.
    ///     <para>
    ///         This is per-field validation, next to the field it guards. Rules that span several fields
    ///         (password confirmation, a date range) belong on the model — through the <c>Form</c>'s
    ///         validator — since no single field owns them.
    ///     </para>
    ///     <para>
    ///         Client-side validation is a convenience, never a control: always validate again on the
    ///         server.
    ///     </para>
    /// </summary>
    Validate<T>? Validate { get; set; }

    /// <summary>
    ///     The <see langword="async" /> form of <see cref="Validate" />, for a rule that has to await
    ///     something — checking a username against the server, say.
    ///     <para>
    ///         Set one or the other, not both: the synchronous rule wins and this is ignored. Remember it
    ///         runs per change, so debounce anything expensive, and let the value through rather than
    ///         blocking the form if the check itself fails.
    ///     </para>
    /// </summary>
    ValidateAsync<T>? ValidateAsync { get; set; }

    /// <summary>
    ///     Runs just after a bound write succeeded, with the value that was written. Use it for the work
    ///     that follows a change — recalculating a total, filtering a dependent list — rather than for
    ///     setting the value itself, which the bind already did.
    ///     <para>
    ///         Bound mode only. In controlled mode the parent already knows, through
    ///         <see cref="OnChange" />.
    ///     </para>
    /// </summary>
    Action<T>? AfterBind { get; set; }

    /// <summary>
    ///     The <see langword="async" /> form of <see cref="AfterBind" />. Both run when both are set — this
    ///     one after the synchronous hook, and awaited before the re-render.
    /// </summary>
    Func<T, Task>? AfterBindAsync { get; set; }

    // Controlled mode — the parent owns Value and is notified of changes.

    /// <summary>
    ///     The control's value, supplied by the parent. This is controlled mode: the value shown is exactly
    ///     what you pass, so the control does not change on its own — handle <see cref="OnChange" />, store
    ///     the new value, and pass it back, or the field appears frozen to the user.
    ///     <para>
    ///         Mutually exclusive with <see cref="Bind" />, which is the simpler choice whenever a model
    ///         property can own the value.
    ///     </para>
    /// </summary>
    T? Value { get; set; }

    /// <summary>
    ///     Called with the new value when the user changes this control, in controlled mode. Store it and
    ///     pass it back through <see cref="Value" /> — the re-render is automatic, so no
    ///     <c>StateHasChanged</c> call is needed.
    /// </summary>
    Action<T>? OnChange { get; set; }

    /// <summary>
    ///     The <see langword="async" /> form of <see cref="OnChange" />. Both run when both are set — this
    ///     one after the synchronous handler.
    /// </summary>
    Func<T, Task>? OnChangeAsync { get; set; }

    // The single delegate the EditContext dispatches — sync or async, whichever the consumer set.
    Delegate? Validator => (Delegate?)Validate ?? ValidateAsync;

    // Registers the per-field validator for the bound field (no-op when context is null). Passing the
    // collapsed Validator each render also clears a stale rule when the consumer drops it, so call it every
    // render. Replaces the boilerplate `ctx?.RegisterFieldValidator(acc.Field, …, () => acc.Getter())` line.
    void RegisterValidator(ExpressionAccessor.Accessor accessor, EditContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.RegisterFieldValidator(accessor.Field, Validator, () => accessor.Getter());
        // Record the binding's authoring component so a two-way write re-renders it (and any derived UI it
        // owns outside the control/Form) automatically — no StateHasChanged on the consumer surface. Prefer
        // the bind expression's root component (`() => _model.Field`); when the bind closed over a loop
        // local (`() => item.Field`, root is a closure, not a component) fall back to the control's creating
        // parent — the component whose Render() authored this control, which is exactly where the derived UI
        // lives. Without the fallback a wrapper control (BsCheck/BsInput/…) would re-render only itself and a
        // sibling deriving from the same model property would go stale.
        context.TrackBindingOwner(accessor.Field,
            accessor.Owner as Component ?? BindingConsumerRegistry.Resolve(this));
    }

    // Runs the post-bind hooks with the freshly-bound value.
    async Task InvokeAfterBindAsync(T value)
    {
        AfterBind?.Invoke(value);
        if (AfterBindAsync is { } hook)
        {
            await hook(value).ConfigureAwait(false);
        }
    }

    // Notifies the controlled-mode consumer of a new value (sync + async).
    async Task InvokeOnChangeAsync(T value)
    {
        OnChange?.Invoke(value);
        if (OnChangeAsync is { } notify)
        {
            await notify(value).ConfigureAwait(false);
        }
    }

    // Bridges a DOM string change to the typed OnChange/OnChangeAsync — parse the raw value to T (identity
    // for string; enums / IParsable<T> round-trip via BindingHelpers.TryParseValue), then notify. Shared by
    // every control's controlled mode; returns null when no controlled change handler is wired. Call it
    // through the interface (`((IFormControl<T>)this).ControlledChangeHandler()`) and register the result as
    // the element's `data-rask-on-change` handler.
    Delegate? ControlledChangeHandler()
    {
        if (OnChange is null && OnChangeAsync is null)
        {
            return null;
        }

        // Re-render the component that OWNS the callback (the consumer), not this control. The
        // control is Element-derived (Select/Input/Textarea), so the generator skips AutoCallback
        // wrapping (the !isElement hot-path guard), and the registered handler's Target is this
        // control — so RegisterHandler's owner heuristic would dirty-mark the control, never the
        // consumer whose state OnChange mutates. Resolve the consumer and notify it after the typed
        // callbacks run.
        //
        // DelegateOwner.Resolve, not a bare `Target as Component`: a handler that captures a local
        // ALONGSIDE `this` — `OnChange: v => Rename(i, v)` inside a loop, or a data grid's per-row
        // checkbox — is lowered to a compiler display class, so `Target as Component` is null and the
        // consumer silently never re-renders. Resolve unwraps the closure's captured `this` (and nested
        // closures) to find the defining component, which is the same rule RegisterHandler and
        // AutoCallback already apply. It also refuses to resolve to an Element, so it cannot regress to
        // dirty-marking the control itself.
        var consumer = DelegateOwner.Resolve(OnChange) ?? DelegateOwner.Resolve(OnChangeAsync);

        return new Func<string, Task>(async raw =>
        {
            if (BindingHelpers.TryParseValue(typeof(T), raw, out var parsed) && parsed is T value)
            {
                await InvokeOnChangeAsync(value).ConfigureAwait(false);
                consumer?.StateHasChanged();
            }
        });
    }
}
