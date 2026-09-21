namespace Rask.Query;

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

/// <summary>What an entry held before a command touched it.</summary>
internal interface IOptimisticSnapshot
{
    void Restore();
}

/// <summary>One projection over the result cached under one key.</summary>
internal sealed class OptimisticEdit<TResult>(SessionQueryClient client, QueryKey key, Func<TResult, TResult> update)
    : OptimisticEdit
{
    internal override IOptimisticSnapshot Apply()
    {
        var had = client.TryGetData<TResult>(key, out var current);
        var snapshot = new Snapshot(client, key, had, current);

        // Nothing cached means nothing on screen to keep consistent, so there is nothing to edit —
        // and inventing a value here would show the user a row the server never confirmed.
        if (had && current is not null)
        {
            client.SetData(key, update(current));
        }

        return snapshot;
    }

    private sealed record Snapshot(SessionQueryClient Client, QueryKey Key, bool Had, TResult? Previous)
        : IOptimisticSnapshot
    {
        public void Restore()
        {
            if (Had && Previous is not null)
            {
                Client.SetData(Key, Previous);
                return;
            }

            // There was nothing to put back, so make the entry fetch rather than leaving whatever the
            // failed command wrote sitting there looking authoritative.
            Client.Invalidate(Key, exact: true);
        }
    }
}
