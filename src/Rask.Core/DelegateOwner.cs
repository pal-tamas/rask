using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Core;

/// <summary>
///     Resolves the <see cref="Component" /> that <em>owns</em> an event-handler delegate — the
///     component whose state the handler mutates and which should therefore re-render after it runs.
///     Used by the DOM handler-owner resolution (<see cref="Component.RegisterHandler(System.Delegate)" />)
///     and by <see cref="AutoCallback" /> so a handler re-renders the component that <em>defined</em> it,
///     not whichever component happens to render the element.
/// </summary>
/// <remarks>
///     <para>
///         The direct case: the delegate <c>Target</c> is the component itself — a method group
///         (<c>OnSubmit: Save</c>) or a lambda that captures only <c>this</c>, which Roslyn lowers to a
///         private instance method on the component. No reflection runs for this (the common) case.
///     </para>
///     <para>
///         The closure case: a lambda that captures <c>this</c> <em>and</em> a local — e.g.
///         <c>() =&gt; _active = index</c> inside a <c>Select((file, index) =&gt; …)</c> — is lowered to a
///         compiler-generated display class, so the delegate <c>Target</c> is that closure and the captured
///         component lives in its <c>&lt;&gt;4__this</c> field. Without unwrapping it, such a handler falls
///         back to the element's render-owner; when the element is nested inside a composite wrapper
///         (e.g. <c>BsCard</c>/<c>BsButton</c>) that owner is the <em>wrapper</em>, so the component holding
///         the state never re-renders. Unwrapping <c>&lt;&gt;4__this</c> makes the defining component the
///         owner regardless of how deeply the element is wrapped.
///     </para>
/// </remarks>
internal static class DelegateOwner
{
    // Roslyn's name for the field a closure uses to hold its captured `this`. Stable across compiler
    // versions; if it ever changes (or the field is absent) we simply return null and the existing
    // CurrentParent fallback applies — no crash, just the pre-existing behaviour.
    private const string CapturedThisField = "<>4__this";

    // The closure-type → captured-`this` FieldInfo lookup, memoised: the set of closure types is fixed
    // (one per lambda site), so reflection runs once per type and every later registration is a dictionary
    // hit. Null means "this closure type has no captured `this`" (captures only locals / statics).
    private static readonly ConcurrentDictionary<Type, FieldInfo?> ThisFieldByClosureType = new();

    public static Component? Resolve(Delegate? handler)
    {
        if (handler?.Target is not { } target)
        {
            return null; // static method / no receiver — nothing to re-render.
        }

        if (target is Component direct)
        {
            return direct; // method group or this-only lambda — no reflection on the hot path.
        }

        // Closure case: unwrap the captured `this`, but ONLY when it is a user component — never an
        // Element. A form control (Input/Select/Textarea, all Element-derived) registers handlers that
        // close over `this` (the control); redirecting their owner to the control would steal the
        // re-render from the consumer the framework already tracks via the binding owner / the explicit
        // `consumer.StateHasChanged()` in IFormControl. Falling back to the element's render-owner
        // (CurrentParent) for those preserves the pre-fix behaviour the forms machinery relies on, while
        // still re-rendering the defining USER component for the case this fix targets (a handler that
        // captures `this` + a local, nested in a composite — e.g. a CodeSample tab click).
        var field = ThisFieldByClosureType.GetOrAdd(target.GetType(), FindCapturedThisField);
        if (field?.GetValue(target) is Component captured and not Element)
        {
            return captured;
        }

        // Nested-closure case: a lambda that captures a loop variable AND `this` — e.g.
        // `items.Select(i => … OnClick: () => Handle(i))` — is lowered to NESTED display classes. The
        // delegate's immediate Target is the inner closure (holding `i`); the captured `this` lives on an
        // OUTER display class it references, so the direct `<>4__this` lookup above misses. Without this,
        // such a handler falls back to the element's render-owner (the composite wrapper, e.g. a BsButton
        // rendered inside a table row), so firing it dirty-marks the wrapper and never re-renders the
        // component that owns the state the handler mutates — the button appears dead. Walk the captured
        // closures to recover the defining component. Only runs when the fast path missed.
        return FindThisInNestedClosures(target, depth: 0);
    }

    // Walks a closure's compiler-generated captured-closure fields (bounded depth) to find the captured
    // component `this`. Returns the first non-Element Component reached — a captured `this` on an outer
    // display class. Guards on display-class field types so it never chases arbitrary captured references.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Enumerates a Roslyn-generated display class's captured fields to recover the " +
            "captured `this`. The compiler emits and preserves these fields on the closure type it also " +
            "instantiates; a trimmed-away field simply isn't found and the caller falls back to the " +
            "element's render-owner — no crash, just the pre-existing behaviour.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The `target.GetType().GetFields()` call reflects over a Roslyn-generated display " +
            "class to recover the captured `this`. The compiler emits and preserves those fields on the " +
            "closure type it also instantiates; a trimmed-away field simply isn't found and the caller " +
            "falls back to the element's render-owner — no crash, just the pre-existing behaviour.")]
    private static Component? FindThisInNestedClosures(object target, int depth)
    {
        if (depth > MaxClosureDepth)
        {
            return null;
        }

        foreach (var f in target.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var value = f.GetValue(target);
            if (value is Component component and not Element)
            {
                return component;
            }

            // Recurse ONLY into captured display classes (compiler-generated closures), never into
            // arbitrary captured objects — a hoisted local could reference anything.
            if (value is not null && IsDisplayClass(value.GetType()) &&
                FindThisInNestedClosures(value, depth + 1) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    // Roslyn names capturing closures `<>c__DisplayClassN_M`; the shared non-capturing lambda cache is
    // `<>c`. Both begin with "<>c", which is enough to gate recursion to compiler-generated closures.
    // Generic display classes are INCLUDED: a closure inside a generic component (e.g. BsMultiSelect<T>'s
    // per-chip `() => ToggleAsync(item)`) is lowered to a generic display class (`<>c__DisplayClassN_M`1`),
    // and its outer display class — the one holding the captured `<>4__this` — is generic too. Excluding
    // generic types here stopped the walk before reaching that `<>4__this`, so Resolve returned null, the
    // callback went unwrapped, and the generic component never re-rendered after its own event fired.
    private static bool IsDisplayClass(Type type) =>
        type.IsClass && type.Name.StartsWith("<>c", StringComparison.Ordinal);

    private const int MaxClosureDepth = 4;

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads the Roslyn-generated '<>4__this' field that holds a closure's captured " +
            "`this`. The compiler writes that field on the closure type it also instantiates, so the " +
            "trimmer preserves it; if it is ever absent GetField returns null and the caller falls back " +
            "to the element's render-owner — no crash, just the pre-existing behaviour.")]
    private static FieldInfo? FindCapturedThisField(Type closureType) =>
        closureType.GetField(
            CapturedThisField,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
}
