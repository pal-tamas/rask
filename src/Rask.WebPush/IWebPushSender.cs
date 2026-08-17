namespace Rask.WebPush;

// Sends a Web Push message to a single subscription: signs the request with VAPID (RFC 8292),
// encrypts the payload with aes128gcm (RFC 8291), and POSTs it to the subscription's endpoint.
// Resolve it from DI after AddRaskWebPush(...). Inspect the returned WebPushResult to decide whether
// to delete (ShouldDelete) or retry (ShouldRetry) the subscription.
/// <summary>
///     Sends a Web Push notification to one subscription: signs with VAPID, encrypts the payload
///     end-to-end, and posts it to the subscription's endpoint. Resolve it from DI after
///     <c>AddRaskWebPush</c>.
/// </summary>
public interface IWebPushSender
{
    /// <summary>
    ///     Sends <paramref name="message" /> to <paramref name="subscription" />.
    /// </summary>
    /// <remarks>
    ///     Delivering to many subscriptions means one call each — and one result each, so act on them
    ///     individually: <see cref="WebPushResult.ShouldDelete" /> means that subscription is dead and
    ///     should be removed, <see cref="WebPushResult.ShouldRetry" /> means try that one again later. A
    ///     failure for one subscriber says nothing about the others.
    ///     <para>
    ///         Success means the push service accepted the message, not that anyone saw it.
    ///     </para>
    /// </remarks>
    /// <param name="subscription">The browser to deliver to.</param>
    /// <param name="message">What to deliver.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The outcome, classified by what to do next.</returns>
    Task<WebPushResult> SendAsync(
        PushSubscription subscription,
        WebPushMessage message,
        CancellationToken cancellationToken = default);
}
