using Microsoft.JSInterop;
using Rask.Core.Live;
using Rask.Wire;

namespace Rask.Core.Browser;

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
    public async ValueTask<NotificationPermissionState> RequestPermissionAsync()
    {
        var result = await js.InvokeAsync<string?>("__raskPush.requestPermission");
        return result switch
        {
            "granted" => NotificationPermissionState.Granted,
            "denied" => NotificationPermissionState.Denied,
            _ => NotificationPermissionState.Default
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
