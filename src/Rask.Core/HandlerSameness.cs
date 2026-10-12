using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Rask.Core.Forms;

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

    // The longest list compared item by item; a longer one is the same list only when it is the same object.
    private const int MaxItems = 256;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Parts = new();

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

    /// <summary>Whether one lambda, written once, made both: two closures of one type over one method.</summary>
    internal static bool FromOneLambda(Delegate a, Delegate b) =>
        a.GetType() == b.GetType()
        && a.Target is { } first
        && b.Target is { } second
        && first.GetType() == second.GetType()
        && DelegateOwner.IsDisplayClass(first.GetType())
        && a.Method == b.Method;

    /// <summary>
    ///     Which of <paramref name="now" /> is the handler <paramref name="sought" /> was, judged by what tells the
    ///     handlers of one lambda apart: the captured values that differ between siblings of one walk. A value every
    ///     sibling shares is what the render worked out for all of them, and says nothing about which one this is.
    /// </summary>
    /// <returns>Its index, or -1 when none is it or more than one could be.</returns>
    /// <param name="sought">The handler the event was sent to.</param>
    /// <param name="then">Every handler that lambda made in the page the event was read from.</param>
    /// <param name="now">Every handler it made in the page as it is.</param>
    internal static int Pick(Delegate sought, List<Delegate> then, List<Delegate> now)
    {
        if (now.Count == 0 || sought.Target is not { } target)
        {
            return -1;
        }

        var parts = Parts.GetOrAdd(target.GetType(), FieldsOf);
        var tells = new bool[parts.Length];
        MarkWhatTellsApart(tells, parts, then);
        MarkWhatTellsApart(tells, parts, now);

        var picked = -1;
        for (var i = 0; i < now.Count; i++)
        {
            if (!SameWhereTold(target, now[i].Target!, parts, tells))
            {
                continue;
            }

            // Nothing tells them apart: they do the same thing, and the first is as good as any.
            if (picked >= 0 && Array.Exists(tells, static told => told))
            {
                return -1;
            }

            picked = picked < 0 ? i : picked;
        }

        return picked;
    }

    private static void MarkWhatTellsApart(bool[] tells, FieldInfo[] parts, List<Delegate> siblings)
    {
        for (var i = 1; i < siblings.Count; i++)
        {
            for (var part = 0; part < parts.Length; part++)
            {
                tells[part] |= !SameCaptured(
                    parts[part].GetValue(siblings[0].Target), parts[part].GetValue(siblings[i].Target), 1);
            }
        }
    }

    private static bool SameWhereTold(object first, object second, FieldInfo[] parts, bool[] tells)
    {
        for (var part = 0; part < parts.Length; part++)
        {
            if (tells[part] && !SameCaptured(parts[part].GetValue(first), parts[part].GetValue(second), 1))
            {
                return false;
            }
        }

        return true;
    }

    // Two objects of one type that are not one object: an adapter around the handler as written, something that
    // says for itself that it equals the other, or something compared part by part.
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

        if (first.Equals(second))
        {
            return true;
        }

        // A list, an array or a dictionary a render fills again: the same when it holds the same things in order.
        if (first is System.Collections.ICollection items)
        {
            return depth < MaxDepth && SameItems(items, (System.Collections.ICollection)second, depth + 1);
        }

        // A bind is written as an expression, built again by every render: the same bind when it reaches the
        // same property of the same object.
        if (first is LambdaExpression bind)
        {
            return SameBind(bind, (LambdaExpression)second);
        }

        if (depth >= MaxDepth || !ComparedByParts(first.GetType()))
        {
            return false;
        }

        return Array.TrueForAll(
            Parts.GetOrAdd(first.GetType(), FieldsOf),
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

    private static bool SameItems(System.Collections.ICollection first, System.Collections.ICollection second, int depth)
    {
        if (first.Count != second.Count || first.Count > MaxItems)
        {
            return false;
        }

        var other = second.GetEnumerator();
        foreach (var item in first)
        {
            if (!other.MoveNext() || !SameCaptured(item, other.Current, depth))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameBind(LambdaExpression first, LambdaExpression second)
    {
        try
        {
            return ExpressionAccessor.Parse(first).Equals(ExpressionAccessor.Parse(second));
        }
        catch (ArgumentException)
        {
            // Not a bind Rask reads: nothing says the two are one.
            return false;
        }
    }

    // What a render makes again and a handler closes over: a closure, a small object or struct of the app's or the
    // kit's that carries a row's values. Never a component — two instances are two components — and never a type
    // of the runtime's own, whose fields are not what it means (a list, a task, a dictionary).
    private static bool ComparedByParts(Type type) =>
        !type.IsPrimitive
        && !type.IsEnum
        && !type.IsArray
        && !typeof(Component).IsAssignableFrom(type)
        && !typeof(Delegate).IsAssignableFrom(type)
        && !IsTheRuntimesOwn(type.Namespace);

    private static bool IsTheRuntimesOwn(string? space) =>
        space is not null
        && (space.StartsWith("System", StringComparison.Ordinal) || space.StartsWith("Microsoft", StringComparison.Ordinal));

    // Every instance field, the ones a base class keeps to itself included.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads the fields of an object a handler closed over. A field the trimmer removed is one " +
            "nothing reads or writes, so nothing a handler does can depend on it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Walks to the base type for the same reason, and with the same consequence.")]
#pragma warning disable S3011 // captured and private fields are what the handler reads: reading them is the comparison
    private static FieldInfo[] FieldsOf(Type type)
    {
        var fields = new List<FieldInfo>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            fields.AddRange(current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }

        return [.. fields];
    }
#pragma warning restore S3011
}
