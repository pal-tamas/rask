using Rask.Cqrs;

namespace Rask.Query;

/// <summary>A cached entry a mutation edits before the server has agreed.</summary>
internal interface IOptimisticUpdate
{
    /// <summary>Applies the edit and returns what is needed to undo it.</summary>
    IOptimisticSnapshot Apply(QueryClient client);
}

/// <summary>What an entry held before a mutation touched it.</summary>
internal interface IOptimisticSnapshot
{
    void Restore(QueryClient client);
}

/// <summary>
///     One optimistic projection over one query's cached result.
/// </summary>
/// <remarks>
///     The snapshot is taken before the edit, not reconstructed after it, because reconstructing
///     means inverting the caller's projection and there is no way to do that in general. Holding the
///     previous value is the only honest undo.
/// </remarks>
internal sealed class OptimisticUpdate<TResult>(IQuery<TResult> message, Func<TResult, TResult> update)
    : IOptimisticUpdate
{
    public IOptimisticSnapshot Apply(QueryClient client)
    {
        var had = client.TryGetData(message, out var current);
        var snapshot = new Snapshot(message, had, current);

        // Nothing cached means nothing on screen to keep consistent, so there is nothing to edit —
        // and inventing a value here would show the user a row the server never confirmed.
        if (had && current is not null)
        {
            client.SetData(message, update(current));
        }

        return snapshot;
    }

    private sealed record Snapshot(IQuery<TResult> Message, bool Had, TResult? Previous) : IOptimisticSnapshot
    {
        public void Restore(QueryClient client)
        {
            if (Had && Previous is not null)
            {
                client.SetData(Message, Previous);
                return;
            }

            // There was nothing to put back, so make the entry fetch rather than leaving whatever the
            // failed mutation wrote sitting there looking authoritative.
            client.Invalidate(Message.GetType());
        }
    }
}
