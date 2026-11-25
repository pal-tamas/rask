using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask.Core;

// The writer case: a handler that captures no component `this` names its owner by the render it is set in.
internal static partial class DelegateOwner
{
    // A type's instance fields that could hold a component, through its base types: memoised like the captured-`this`
    // field, so the question costs a dictionary hit once a type has been asked once. Empty for a closure over locals that
    // hold none — the common case, which then reads no field at all.
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsThatMayHoldAComponentByType = new();

    // The component whose Render wrote an element's handler that holds no other (NoteWriter). Weak on the handler: an
    // element and its handlers are built afresh every render, and an entry goes with the delegate it is about.
    private static readonly ConditionalWeakTable<Delegate, Component> WrittenIn = new();

    /// <summary>
    ///     <see cref="Resolve" />, else — for a handler that holds no component but the one rendering — the component
    ///     whose Render is running: the one that wrote it.
    /// </summary>
    /// <remarks>
    ///     A lambda that captures only a local — <c>var rows = Rows; … () =&gt; rows.Add(row)</c> — or a method group on
    ///     something that is not a component (<c>rows.Clear</c>) carries no <c>this</c> that names the component whose
    ///     state it changes. Asked while that component renders, as a chain step setting the handler is, the render says
    ///     it. So it does when the lambda shares its closure with a delegate of that component's own — the lambdas of one
    ///     method share one closure, so <c>() =&gt; rows.Add(row)</c> beside <c>row =&gt; name(row)</c> holds <c>name</c>,
    ///     a method of the writer's. A handler holding any OTHER component, and one set outside a render, are left to
    ///     <see cref="Resolve" />.
    /// </remarks>
    internal static Component? ResolveWhileRendering(Delegate? handler) =>
        Resolve(handler) ?? (handler is null ? null : WrittenBy(handler));

    /// <summary>
    ///     Notes the component whose Render is setting <paramref name="handler" /> on an element, when the handler holds
    ///     no other: the walk registers it later, inside whatever composite the element was handed to, and that composite
    ///     is not the component whose state the handler changes.
    /// </summary>
    internal static void NoteWriter(Delegate handler)
    {
        if (handler.Target is null or Component || WrittenBy(handler) is not { } writer)
        {
            return;
        }

        // Under the handler as written: Resolve reads through the adapters it is held in before it asks.
        WrittenIn.AddOrUpdate(Written(handler), writer);
    }

    // The component rendering now, when the handler — through the adapters it may be held in — holds no component but it.
    // A static method has no target and is nobody's.
    private static Component? WrittenBy(Delegate handler)
    {
        if (Written(handler).Target is not { } target || target is Component or IHandlerAdapter)
        {
            return null;
        }

        var fields = FieldsThatMayHoldAComponent(target.GetType());
        return Rendering() is { } writer && (fields.Length == 0 || HoldsOnly(target, fields, writer, depth: 0)) ? writer : null;
    }

    // The component rendering now, never an element: an element's own handlers close over the element.
    private static Component? Rendering() =>
        LiveRenderContext.RenderOwner(out _) is { } rendering and not Element ? rendering : null;

    // The handler as the call site wrote it, out of the adapters it is held in.
    private static Delegate Written(Delegate handler)
    {
        for (var depth = 0; handler.Target is IHandlerAdapter adapter && depth <= MaxClosureDepth; depth++)
        {
            handler = adapter.Inner;
        }

        return handler;
    }

    // Whether every component the holder's fields reach is the writer. Bounded: what lies deeper than a closure nested
    // MaxClosureDepth times is taken to hold another, and the handler is left to Resolve.
    // Only reached for a target with such a field; a closure over plain locals never gets here. A plain loop, not All:
    // it runs as a handler is set during a render, and a lambda over the holder would allocate on every one.
    private static bool HoldsOnly(object holder, FieldInfo[] fields, Component writer, int depth)
    {
        var only = true;
        for (var i = 0; only && i < fields.Length; i++)
        {
            only = HoldsOnly(fields[i].GetValue(holder), writer, depth);
        }

        return only;
    }

    private static bool HoldsOnly(object? value, Component writer, int depth)
    {
        if (value is null or Component)
        {
            return value is null || ReferenceEquals(value, writer);
        }

        if (depth >= MaxClosureDepth)
        {
            return false;
        }

        // A method of the writer's (`name = Named`), a lambda over what it holds, or a static method.
        if (value is Delegate held)
        {
            return held.HasSingleTarget && (held.Target is not { } target || HoldsOnly(target, writer, depth + 1));
        }

        var fields = FieldsThatMayHoldAComponent(value.GetType());
        return fields.Length == 0 || HoldsOnly(value, fields, writer, depth + 1);
    }

    private static FieldInfo[] FieldsThatMayHoldAComponent(Type type) =>
        FieldsThatMayHoldAComponentByType.GetOrAdd(type, FindFieldsThatMayHoldAComponent);

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads the declared types of a handler target's fields to tell which could hold a " +
            "component. A trimmed-away field is not there to hold one either; at worst the answer is \"holds none\" " +
            "for a target whose field was trimmed, which re-renders the component that wrote the handler.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The same read, of the base types a closure's fields are declared on: what the trimmer took " +
            "away holds no component.")]
    private static FieldInfo[] FindFieldsThatMayHoldAComponent(Type type)
    {
        var found = new List<FieldInfo>();
        for (var declaring = type; declaring is not null && declaring != typeof(object); declaring = declaring.BaseType)
        {
#pragma warning disable S3011 // a closure's captured fields are compiler-generated and private: reading them IS the lookup
            var fields = declaring.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
#pragma warning restore S3011
            found.AddRange(fields.Where(static field => MayHoldAComponent(field.FieldType)));
        }

        return [.. found];
    }

    // A field of this type could reference a component: a component type, a base of one (object, an interface it
    // implements), a delegate that may close over one, or another closure.
    private static bool MayHoldAComponent(Type held) =>
        held.IsAssignableFrom(typeof(Component)) || typeof(Component).IsAssignableFrom(held)
        || typeof(Delegate).IsAssignableFrom(held) || IsDisplayClass(held);
}
