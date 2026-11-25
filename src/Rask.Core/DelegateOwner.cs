using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rask.Core.Live;

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
///         (<c>OnAnySubmit: Save</c>) or a lambda that captures only <c>this</c>, which Roslyn lowers to a
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
///     <para>
///         The writer case: a lambda that captures only a local — <c>var rows = Rows; … () =&gt; rows.Add(row)</c>
///         — holds no component at all, so nothing on the delegate names its owner. It is set while its
///         component renders, though: <see cref="NoteWriter" /> notes that component for an element's
///         handler, and <see cref="ResolveWhileRendering" /> answers with it for a callback a chain step wraps.
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

    // Whether a handler's target type has no instance field that could hold a component, or a closure that might:
    // memoised like the field above, so the question costs a dictionary hit once a type has been asked once.
    private static readonly ConcurrentDictionary<Type, bool> HoldsNoComponentByType = new();

    // The component whose Render wrote an element's handler that holds none (NoteWriter). Weak on the handler: an
    // element and its handlers are built afresh every render, and an entry goes with the delegate it is about.
    private static readonly ConditionalWeakTable<Delegate, Component> WrittenIn = new();

    public static Component? Resolve(Delegate? handler)
    {
        if (handler?.Target is not { } target)
        {
            return null; // static method / no receiver — nothing to re-render.
        }

        // A ValueTask handler is held as a Task-returning adapter; the owner is the one who wrote the handler.
        if (target is IHandlerAdapter adapter)
        {
            return Resolve(adapter.Inner);
        }

        if (target is Component direct)
        {
            return direct; // method group or this-only lambda — no reflection on the hot path.
        }

        // An element's handler that holds no component, written in a component's Render (NoteWriter).
        if (WrittenIn.TryGetValue(handler, out var writer))
        {
            return writer;
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

    /// <summary>
    ///     <see cref="Resolve" />, else — for a handler that holds no component at all — the component whose Render is
    ///     running: the one that wrote it.
    /// </summary>
    /// <remarks>
    ///     A lambda that captures only a local — <c>var rows = Rows; … () =&gt; rows.Add(row)</c> — or a method group on
    ///     something that is not a component (<c>rows.Clear</c>) carries nothing that names the component whose state
    ///     it changes. Asked while that component renders, as a chain step setting the handler is, the render says it.
    ///     A handler that might hold a component, and one set outside a render, are left to <see cref="Resolve" />.
    /// </remarks>
    internal static Component? ResolveWhileRendering(Delegate? handler) =>
        Resolve(handler) ?? (handler is not null && HoldsNoComponent(handler) ? Rendering() : null);

    /// <summary>
    ///     Notes the component whose Render is setting <paramref name="handler" /> on an element, when the handler holds
    ///     none: the walk registers it later, inside whatever composite the element was handed to, and that composite is
    ///     not the component whose state the handler changes.
    /// </summary>
    internal static void NoteWriter(Delegate handler)
    {
        if (handler.Target is null or Component || !HoldsNoComponent(handler) || Rendering() is not { } writer)
        {
            return;
        }

        // Under the handler as written: Resolve reads through the adapters it is held in before it asks.
        WrittenIn.AddOrUpdate(Written(handler), writer);
    }

    // The component rendering now, never an element: an element's own handlers close over the element.
    private static Component? Rendering() =>
        LiveRenderContext.RenderOwner(out _) is { } rendering and not Element ? rendering : null;

    // Whether nothing the handler holds could be a component: its target, through the adapters it may be held in, has
    // no instance field typed to hold one, or a closure that might. A static method has no target and holds nothing.
    private static bool HoldsNoComponent(Delegate handler) =>
        Written(handler).Target is { } target and not (Component or IHandlerAdapter)
        && HoldsNoComponentByType.GetOrAdd(target.GetType(), static type => !FieldsMayHoldAComponent(type));

    // The handler as the call site wrote it, out of the adapters it is held in.
    private static Delegate Written(Delegate handler)
    {
        for (var depth = 0; handler.Target is IHandlerAdapter adapter && depth <= MaxClosureDepth; depth++)
        {
            handler = adapter.Inner;
        }

        return handler;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads the declared types of a handler target's fields to tell that none could hold a " +
            "component. A trimmed-away field is not there to hold one either; at worst the answer is \"holds none\" " +
            "for a target whose field was trimmed, which re-renders the component that wrote the handler.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The same read, of the base types a closure's fields are declared on: what the trimmer took " +
            "away holds no component.")]
    private static bool FieldsMayHoldAComponent(Type type)
    {
        for (var declaring = type; declaring is not null && declaring != typeof(object); declaring = declaring.BaseType)
        {
#pragma warning disable S3011 // a closure's captured fields are compiler-generated and private: reading them IS the lookup
            var fields = declaring.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
#pragma warning restore S3011
            if (fields.Any(field => MayHoldAComponent(field.FieldType)))
            {
                return true;
            }
        }

        return false;
    }

    // A field of this type could reference a component: a component type, a base of one (object, an interface it
    // implements), a delegate that may close over one, or another closure.
    private static bool MayHoldAComponent(Type held) =>
        held.IsAssignableFrom(typeof(Component)) || typeof(Component).IsAssignableFrom(held)
        || typeof(Delegate).IsAssignableFrom(held) || IsDisplayClass(held);

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

#pragma warning disable S3011 // a closure's captured fields are compiler-generated and private: reading them IS the lookup
        foreach (var f in target.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
#pragma warning restore S3011
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
    internal static bool IsDisplayClass(Type type) =>
        type.IsClass && type.Name.StartsWith("<>c", StringComparison.Ordinal);

    private const int MaxClosureDepth = 4;

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads the Roslyn-generated '<>4__this' field that holds a closure's captured " +
            "`this`. The compiler writes that field on the closure type it also instantiates, so the " +
            "trimmer preserves it; if it is ever absent GetField returns null and the caller falls back " +
            "to the element's render-owner — no crash, just the pre-existing behaviour.")]
#pragma warning disable S3011 // the compiler's captured-`this` field on a closure type is not public
    private static FieldInfo? FindCapturedThisField(Type closureType) =>
        closureType.GetField(
            CapturedThisField,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
#pragma warning restore S3011
}
