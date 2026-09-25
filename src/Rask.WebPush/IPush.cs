using System.ComponentModel;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>
/// The Web Push battery: the browsers that subscribed, on the app's database, and a send that reaches them.
/// </summary>
/// <remarks>
/// Inject it, or reach it through <see cref="Push" /> with nothing injected. <see cref="IWebPush" /> underneath is
/// the sender for one subscription; this is the layer that knows who subscribed.
/// </remarks>
public interface IPush
{
    /// <summary>The VAPID public key a browser subscribes with, or <see langword="null" /> until a key pair is configured.</summary>
    string? PublicKey { get; }

    /// <summary>Keeps a browser's subscription, for the signed-in user when there is one. Subscribing again renews the row.</summary>
    Task<PushSubscriber> Subscribe(PushSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Forgets a browser's subscription. <see langword="false" /> when it was not there.</summary>
    Task<bool> Unsubscribe(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Sends to one subscription and reports how the push service answered.</summary>
    Task<WebPushResult> Send(PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends to every subscriber, or to one user's browsers, dropping the subscriptions the push service says are
    /// gone. Returns how many were delivered. Reached through <c>Push.Send(message)</c> and its <c>.To(userId)</c>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<int> Deliver(WebPushMessage message, Guid? userId, CancellationToken cancellationToken = default);
}
