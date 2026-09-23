namespace Rask.Cqrs;

/// <summary>
///     Opens a subscription on the server, for a client whose notifications are published there. Implemented by
///     <c>Rask.Cqrs.Client</c> and registered by <c>AddRaskCqrsClient</c>; the dispatcher prefers it for any
///     notification that has a wire contract.
/// </summary>
internal interface IRemoteSubscriptions
{
    /// <summary>
    ///     Streams the server's notifications of <paramref name="contract" />'s type for <paramref name="key" />, calling
    ///     <paramref name="connected" /> once the server has admitted the subscription. Ends by throwing when the
    ///     connection drops; the caller decides whether to open another.
    /// </summary>
    IAsyncEnumerable<INotification> Subscribe(
        RemoteContract contract,
        object? key,
        Action? connected,
        CancellationToken cancellationToken);
}
