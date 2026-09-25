using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to local notifications (the Notifications API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API" />) — show a
///     notification directly from the page (no server / push needed). Works on both hosts; on Server,
///     trigger <see cref="RequestPermissionAsync" /> from an event handler so the prompt rides a user
///     gesture. For notifications delivered while the app is closed, use <see cref="IWebPush" /> (push
///     goes through the service worker).
/// </summary>
/// <remarks>
///     Requires a secure context. Gate on <see cref="IsSupportedAsync" /> /
///     <see cref="RequestPermissionAsync" /> and wrap in try/catch — an unsupported browser or denied
///     permission surfaces as a <see cref="JSException" />.
/// </remarks>
public interface INotifications
{
    /// <summary>Whether the browser supports notifications.</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>The current permission without prompting (<c>Notification.permission</c>).</summary>
    ValueTask<NotificationPermissionState> PermissionAsync();

    /// <summary>Prompts for (or reports) notification permission (<c>Notification.requestPermission</c>).</summary>
    ValueTask<NotificationPermissionState> RequestPermissionAsync();

    /// <summary>Shows a notification (<c>new Notification(title, options)</c>). Requires granted permission.</summary>
    ValueTask ShowAsync(string title, NotificationOptions? options = null);
}
