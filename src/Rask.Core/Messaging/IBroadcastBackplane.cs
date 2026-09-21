namespace Rask.Core.Messaging;

/// <summary>
///     Carries the messages of cross-host topics between the hosts of one app (#1115). <see cref="BroadcastHub" /> hands
///     it a publish already serialized, and it hands back what the other hosts published. Implemented by
///     <c>Rask.Redis</c>; with none registered, every topic stays inside its process.
/// </summary>
/// <remarks>
///     A backplane never returns a host's own publish to it: the hub delivers a publish locally before handing it over,
///     so an echo would reach this host's subscribers twice.
/// </remarks>
internal interface IBroadcastBackplane
{
    /// <summary>Sends <paramref name="payload" /> to the other hosts' subscribers of <paramref name="topic" />.</summary>
    Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken);

    /// <summary>
    ///     Starts receiving <paramref name="topic" />: every payload another host publishes on it is passed to
    ///     <paramref name="received" />, one at a time and in the order they arrive. Called once per topic.
    /// </summary>
    void Subscribe(string topic, Action<ReadOnlyMemory<byte>> received);
}
