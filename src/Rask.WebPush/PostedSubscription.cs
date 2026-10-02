using Rask.Wire;

namespace Rask.WebPush;

// What a browser posts to /_rask/push/subscribe: MDN's PushSubscriptionJSON, the shape `subscription.toJSON()` answers
// ({endpoint, expirationTime, keys: {p256dh, auth}}) and what Rask.Web's PushSubscription.ToJSON() reads back as. The
// flat {endpoint, p256dh, auth} — PushSubscription's own shape — is read too, so a client that posts the record it
// holds still subscribes. A key missing from both arrives empty, and Push.Subscribe refuses it.
internal sealed record PostedSubscription(string? Endpoint, PostedSubscription.KeyPair? Keys, string? P256dh, string? Auth, double? ExpirationTime)
{
    public PushSubscription Subscription =>
        new(Endpoint ?? "", Keys?.P256dh ?? P256dh ?? "", Keys?.Auth ?? Auth ?? "", ExpirationTime);

    internal sealed record KeyPair(string? P256dh, string? Auth);
}
