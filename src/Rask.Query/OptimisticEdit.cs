namespace Rask.Querying;

/// <summary>
///     An edit to one query's cached result, shown before the server answers and undone if it refuses.
///     Made by <see cref="Query{TResult}.Optimistic" /> and handed to a command's <c>SendAsync</c>.
/// </summary>
/// <remarks>
///     <para>
///         Made per send, from the query already on screen, so the edit sees what is being sent and aims
///         at whatever that query shows now — a message query or a function one alike:
///     </para>
///     <code>
///     ship.SendAsync(new ShipOrder(id), orders.Optimistic(list =&gt; [.. list.Where(o =&gt; o.Id != id)]));
///     </code>
///     <para>
///         On success the command's invalidation refetches and the truth replaces the guess; on failure the
///         previous value is put back. The value is snapshotted before the edit rather than reconstructed
///         after it, because a caller's projection cannot be inverted — holding the previous value is the
///         only honest undo.
///     </para>
/// </remarks>
public abstract class OptimisticEdit
{
    private protected OptimisticEdit()
    {
    }

    /// <summary>Applies the edit and returns what is needed to undo it.</summary>
    internal abstract IOptimisticSnapshot Apply();
}
