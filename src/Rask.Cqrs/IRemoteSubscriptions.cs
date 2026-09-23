namespace Rask.Cqrs;

/// <summary>
///     Opens a subscription on the server, for a client whose notifications are published there. Implemented by
///     <c>Rask.Cqrs.Client</c> and registered by <c>AddRaskCqrsClient</c>; the dispatcher prefers it for any
///     notification that has a wire contract.
/// </summary>
internal interface IRemoteSubscriptions
{
    /// <summary>
    ///     Streams what <paramref name="contract" /> opens — the notifications <paramref name="subscription" /> asks for,
    ///     or every one of the contract's own notification type when it is null — calling <paramref name="connected" />
    ///     once the server has admitted it. Ends by throwing when the connection drops; the caller decides whether to
    ///     open another.
    /// </summary>
    IAsyncEnumerable<INotification> Subscribe(
        RemoteContract contract,
        object? subscription,
        Action? connected,
        CancellationToken cancellationToken);
}
