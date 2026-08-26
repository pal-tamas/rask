namespace Rask.Query;

/// <summary>
///     Names the queries a command makes out of date, so dispatching it through
///     <see cref="IQueryClient.MutateAsync(Rask.Cqrs.ICommand, System.Threading.CancellationToken)" />
///     refetches them wherever they are on screen.
/// </summary>
/// <remarks>
///     <para>
///         Declared on the command rather than passed at the call site, because the relationship
///         belongs to the thing that causes it: a new screen that ships the same command gets the
///         same invalidation for free, and adding a query that a command affects is one edit in one
///         place rather than a hunt through call sites. It also shows up in a diff, which "we forgot
///         to invalidate the list" never does.
///     </para>
///     <para>
///         A stale list after a save that clearly succeeded is the most common complaint about every
///         cache of this kind, and it is almost always a missing invalidation somebody had to
///         remember to write.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [Invalidates(typeof(GetOrders), typeof(GetOrderCount))]   // two messages
///     [Invalidates("orders")]                                    // everything under that prefix
///     public sealed record ShipOrder(Guid Id) : ICommand;
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class InvalidatesAttribute : Attribute
{
    /// <summary>Names the query message types this command makes out of date.</summary>
    /// <remarks>
    ///     Several types are several prefixes — each one invalidates every entry for that message,
    ///     whatever its arguments.
    /// </remarks>
    /// <param name="queryTypes">The query message types to refetch after this command succeeds.</param>
    public InvalidatesAttribute(params Type[] queryTypes)
    {
        QueryTypes = queryTypes;
        KeyPrefix = [];
    }

    /// <summary>Names a key prefix this command makes out of date.</summary>
    /// <remarks>
    ///     Several strings are ONE prefix of several parts, not several prefixes — the asymmetry with the
    ///     type form is deliberate, because each reads the way its own thing is written:
    ///     <c>[Invalidates(typeof(A), typeof(B))]</c> names two messages, <c>[Invalidates("orders",
    ///     "summary")]</c> names one path. Repeat the attribute for more than one.
    /// </remarks>
    /// <param name="keyPrefix">The parts of the key prefix to invalidate.</param>
    public InvalidatesAttribute(params string[] keyPrefix)
    {
        QueryTypes = [];
        KeyPrefix = keyPrefix;
    }

    /// <summary>The query message types this command makes out of date.</summary>
    public IReadOnlyList<Type> QueryTypes { get; }

    /// <summary>The parts of the key prefix this command makes out of date, if any.</summary>
    public IReadOnlyList<string> KeyPrefix { get; }
}
