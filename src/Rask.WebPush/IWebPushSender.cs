namespace Rask.WebPush;

// Sends a Web Push message to a single subscription: signs the request with VAPID (RFC 8292),
// encrypts the payload with aes128gcm (RFC 8291), and POSTs it to the subscription's endpoint.
// Resolve it from DI after AddRaskWebPush(...). Inspect the returned WebPushResult to decide whether
// to delete (ShouldDelete) or retry (ShouldRetry) the subscription.
public interface IWebPushSender
{
    Task<WebPushResult> SendAsync(
        PushSubscription subscription,
        WebPushMessage message,
        CancellationToken cancellationToken = default);
}
