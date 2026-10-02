using Rask.Web.Types;

namespace Rask.WebPush;

/// <summary>
/// <c>await Push.Subscribe(await subscription.ToJSON())</c>: a component on the server host keeps the subscription MDN's
/// <c>PushManager</c> handed it. Here rather than in Rask.WebPush because only a host carries both halves — the browser
/// API (Rask.Web) and the battery.
/// </summary>
public static class BrowserPushSubscriptions
{
    extension(Push)
    {
        /// <summary>Keeps a browser's subscription, as MDN's <c>toJSON()</c> answers it, for the signed-in user.</summary>
        public static Task<PushSubscriber> Subscribe(PushSubscriptionJSON subscription, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);

            // MDN nests the two keys; a missing one arrives empty, and Push.Subscribe refuses it by name.
            var keys = subscription.Keys;
            var kept = new Wire.PushSubscription(
                subscription.Endpoint ?? "", keys?.GetValueOrDefault("p256dh") ?? "", keys?.GetValueOrDefault("auth") ?? "", subscription.ExpirationTime);
            return Push.Subscribe(kept, cancellationToken);
        }
    }
}
