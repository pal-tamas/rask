using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Core;

/// <summary>
///     Whether two handlers made by two renders are the same handler: the same method, over the same receiver or
///     over closures that captured equal values.
/// </summary>
/// <remarks>
///     <para>
///         A render makes its lambdas again, so the handler a slot holds after it is never the object it held
///         before. <c>() =&gt; Remove(row)</c> written once is the same handler in both when <c>row</c> is — and a
///         different one when the rows moved up and the slot now closes over the next row.
///     </para>
///     <para>
///         Captured values are compared with <see cref="object.Equals(object, object)" />: the same instance, or a
///         type that says two instances are equal (a number, a string, a record). A row read again from a store
///         into a new object of a class is not equal to the one before it, and the answer is no — which is the safe
///         one: nothing runs.
///     </para>
///     <para>
///         Costs a method lookup and a boxed read per captured value, so it never runs for every handler of a walk:
///         only for an event that arrived from an older page, and for the few walks after one.
///     </para>
/// </remarks>
internal static class HandlerSameness
{
    // A closure that captured closures that captured closures: deeper than any lambda nests in practice.
    private const int MaxDepth = 6;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> CapturedFields = new();

    internal static bool Same(Delegate a, Delegate b) => Same(a, b, 0);

    private static bool Same(Delegate a, Delegate b, int depth)
    {
        if (ReferenceEquals(a, b) || a.Equals(b))
        {
            return true;
        }

        if (a.GetType() != b.GetType()
            || a.Target is not { } first
            || b.Target is not { } second
            || first.GetType() != second.GetType()
            || a.Method != b.Method)
        {
            return false;
        }

        return SameReceiver(first, second, depth);
    }

    // Two receivers of one type that are not one object: an adapter around the handler as written, a closure, or
    // something that has to say for itself whether it equals the other.
    private static bool SameReceiver(object first, object second, int depth)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is IHandlerAdapter adapter)
        {
            return depth < MaxDepth && Same(adapter.Inner, ((IHandlerAdapter)second).Inner, depth + 1);
        }

        if (!IsClosure(first.GetType()))
        {
            return first.Equals(second);
        }

        if (depth >= MaxDepth)
        {
            return false;
        }

        return Array.TrueForAll(
            CapturedFields.GetOrAdd(first.GetType(), FieldsOf),
            field => SameCaptured(field.GetValue(first), field.GetValue(second), depth + 1));
    }

    private static bool SameCaptured(object? first, object? second, int depth)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null || second is null || first.GetType() != second.GetType())
        {
            return false;
        }

        return first is Delegate handler
            ? Same(handler, (Delegate)second, depth)
            : SameReceiver(first, second, depth);
    }

    // Roslyn names a capturing closure `<>c__DisplayClassN_M`. The cache of lambdas that capture nothing is `<>c`,
    // and has one instance, so it never gets this far.
    private static bool IsClosure(Type type) => type.IsClass && type.Name.StartsWith("<>c", StringComparison.Ordinal);

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads the fields of a compiler-generated closure, which the lambda that the closure's " +
            "delegate points at reads too, so the trimmer keeps them. A field that is neither read nor written " +
            "anywhere holds nothing a handler's behaviour could depend on.")]
#pragma warning disable S3011 // a closure's captured fields are compiler-generated: reading them is the comparison
    private static FieldInfo[] FieldsOf(Type closure) =>
        closure.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
#pragma warning restore S3011
}
