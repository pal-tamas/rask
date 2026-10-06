using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>
/// Web Push with nothing injected: <c>await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way", "/orders/1042"))</c>
/// reaches every subscribed browser; <c>.To(userId)</c> reaches one person's. Resolved from the work in progress —
/// a handler, a render, a request, a job — like <c>Mail.Send</c>. Inject <see cref="IPush" /> where you would rather.
/// </summary>
public static class Push
{
    /// <summary>Sends to every subscriber — or, with <see cref="Pushing.To" />, to one user's browsers. Awaiting it returns how many were delivered.</summary>
    public static Pushing Send(WebPushMessage message, CancellationToken cancellationToken = default) =>
        new(null, message ?? throw new ArgumentNullException(nameof(message)), null, cancellationToken);

    /// <summary>Sends to one subscription and reports how the push service answered.</summary>
    public static Task<WebPushResult> Send(
        PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default) =>
        Resolve().Send(subscription, message, Ambient.Or(cancellationToken));

    /// <summary>Keeps a browser's subscription for the signed-in user. On the server host this is the line after MDN's <c>PushManager.Subscribe</c>.</summary>
    public static Task<PushSubscriber> Subscribe(PushSubscription subscription, CancellationToken cancellationToken = default) =>
        Resolve().Subscribe(subscription, Ambient.Or(cancellationToken));

    /// <summary>Forgets a browser's subscription.</summary>
    public static Task<bool> Unsubscribe(string endpoint, CancellationToken cancellationToken = default) =>
        Resolve().Unsubscribe(endpoint, Ambient.Or(cancellationToken));

    /// <summary>The VAPID public key a browser subscribes with, or <see langword="null" /> until a key pair is configured.</summary>
    public static string? PublicKey => Resolve().PublicKey;

    /// <summary>Whether the battery is registered. Hidden: an app should not branch on it — a call with nothing registered throws and names the fix.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn =>
        Faked.Value is not null || Ambient.Services?.GetService<IPush>() is not null;

    internal static readonly AsyncLocal<IPush?> Faked = new();

    internal static IPush Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        return Ambient.Reach<IPush>("Push", "Program.cs says c.Push.Off() or Rask:Push has no VAPID keys", "AddRaskWebPush<AppDbContext>()");
    }
}
