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

        var field = ThisFieldByClosureType.GetOrAdd(target.GetType(), FindCapturedThisField);
        return field?.GetValue(target) as Component;
    }

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
