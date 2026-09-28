namespace Rask.Querying;

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
            client.Set(key, update(current));
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
                Client.Set(Key, Previous);
                return;
            }

            // There was nothing to put back, so make the entry fetch rather than leaving whatever the
            // failed command wrote sitting there looking authoritative.
            Client.Invalidate(Key.Only());
        }
    }
}
