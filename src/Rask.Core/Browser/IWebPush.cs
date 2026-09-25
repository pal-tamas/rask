using Microsoft.JSInterop;
using Rask.Core.Live;
using Rask.Wire;

namespace Rask.Core.Browser;

/// <summary>The user's decision on a notification-permission prompt (<c>Notification.permission</c>).</summary>
public enum NotificationPermission
{
    /// <summary>Not yet decided — the browser will prompt on the next request.</summary>
    Default,

    /// <summary>Granted; notifications (and push) are allowed.</summary>
    Granted,

    /// <summary>Denied; blocked until the user changes the site setting.</summary>
    Denied
}

/// <summary>
///     Typed access to the Web Push API (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Push_API" />).
///     Works on both hosts: push relies on a Service Worker that runs independently of any page, which the
///     WASM host always ships and the Server host serves once PWA is opted into (<c>AddRaskPwa</c>). Inject
///     it through a component constructor and drive it from event handlers:
///     <code>
///     if (await push.IsSupportedAsync() &amp;&amp; await push.RequestPermissionAsync() == NotificationPermission.Granted)
///     {
///         await push.RegisterServiceWorkerAsync();
///         var sub = await push.SubscribeAsync(vapidPublicKey);   // POST sub to your backend
///     }
///     </code>
/// </summary>
/// <remarks>
///     Requires a secure context (HTTPS or localhost). Unsupported browsers / denied permission surface
///     as a <see cref="JSException" /> from the awaited task — gate on <see cref="IsSupportedAsync" /> and
///     wrap calls in try/catch. Rask ships a default service worker (<c>rask-sw.js</c>) that displays
///     the pushed notification; pass your own URL to <see cref="RegisterServiceWorkerAsync" /> to override.
/// </remarks>
public interface IWebPush
{
    /// <summary>Whether this browser supports service workers, the Push API, and notifications.</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Prompts for (or reports) notification permission (<c>Notification.requestPermission</c>).</summary>
    ValueTask<NotificationPermission> RequestPermissionAsync();

    /// <summary>
    ///     Registers the service worker that receives pushes. <paramref name="swUrl" /> defaults to the
    ///     framework's <c>{PathBase}/rask-sw.js</c>.
    /// </summary>
    ValueTask RegisterServiceWorkerAsync(string? swUrl = null);

    /// <summary>
    ///     Subscribes to push for this browser, returning the subscription to hand to your backend
    ///     (<c>pushManager.subscribe</c>). <paramref name="vapidPublicKey" /> is your VAPID application
    ///     server key (base64url). Register the service worker first.
    /// </summary>
    ValueTask<PushSubscription> SubscribeAsync(string vapidPublicKey);

    /// <summary>Returns the current push subscription, or <c>null</c> if not subscribed.</summary>
    ValueTask<PushSubscription?> GetSubscriptionAsync();

    /// <summary>Unsubscribes from push; returns whether a subscription was removed.</summary>
    ValueTask<bool> UnsubscribeAsync();
}

/// <summary>
///     Default <see cref="IWebPush" />, backed by the unified <see cref="IJSRuntime" /> via the framework's
///     <c>__raskPush.*</c> helpers (which wrap the service-worker registration, VAPID key decoding, and
///     subscription serialization that <c>IJSRuntime</c> can't express directly).
/// </summary>
public sealed class WebPush(IJSRuntime js) : IWebPush
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskPush.isSupported");

    /// <inheritdoc />
    public async ValueTask<NotificationPermission> RequestPermissionAsync()
    {
        var result = await js.InvokeAsync<string?>("__raskPush.requestPermission");
        return result switch
        {
            "granted" => NotificationPermission.Granted,
            "denied" => NotificationPermission.Denied,
            _ => NotificationPermission.Default
        };
    }

    /// <inheritdoc />
    public ValueTask RegisterServiceWorkerAsync(string? swUrl = null) =>
        js.InvokeVoidAsync("__raskPush.register", swUrl ?? $"{LiveOptions.PathBase}/rask-sw.js");

    /// <inheritdoc />
    public ValueTask<PushSubscription> SubscribeAsync(string vapidPublicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(vapidPublicKey);
        return js.InvokeAsync<PushSubscription>("__raskPush.subscribe", vapidPublicKey);
    }

    /// <inheritdoc />
    public ValueTask<PushSubscription?> GetSubscriptionAsync() =>
        js.InvokeAsync<PushSubscription?>("__raskPush.getSubscription");

    /// <inheritdoc />
    public ValueTask<bool> UnsubscribeAsync() => js.InvokeAsync<bool>("__raskPush.unsubscribe");
}
