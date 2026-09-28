namespace Rask.Querying;

/// <summary>A lambda re-run at every read, answering what the subscription should watch now.</summary>
internal interface ISubscriptionSource<T>
{
    /// <summary>True when the answer changed since the last read; <paramref name="target" /> null means "wait".</summary>
    bool TryAdvance(out SubscriptionTarget<T>? target);
}
