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
///     [Invalidates(typeof(GetOrders), typeof(GetOrderCount))]
///     public sealed record ShipOrder(Guid Id) : ICommand;
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class InvalidatesAttribute : Attribute
{
    /// <summary>Names the query message types this command makes out of date.</summary>
    /// <param name="queryTypes">The query message types to refetch after this command succeeds.</param>
    public InvalidatesAttribute(params Type[] queryTypes) => QueryTypes = queryTypes;

    /// <summary>The query message types this command makes out of date.</summary>
    public IReadOnlyList<Type> QueryTypes { get; }
}
