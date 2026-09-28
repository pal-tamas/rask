namespace Rask.Querying;

/// <summary>What a subscription watches, and how to open it.</summary>
/// <param name="Identity">Compared to decide whether a new call watches something else: the type and key, or the input.</param>
/// <param name="Open">Opens the stream, calling its argument once the stream is admitted.</param>
internal sealed record SubscriptionTarget<T>(object Identity, Func<Action, CancellationToken, IAsyncEnumerable<T>> Open);
