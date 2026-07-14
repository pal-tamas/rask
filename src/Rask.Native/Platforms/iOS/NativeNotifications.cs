using Foundation;
using Rask.Core.Browser;
using UserNotifications;

namespace Rask.Native;

// Native iOS backend for INotifications — the OS notification centre (UNUserNotificationCenter) instead of
// the WebView's Notification constructor, which WKWebView doesn't expose at all. Registered by ApplePlatform;
// the framework resolves it over the JS-backed default (native-first). ShowAsync posts a local notification
// for immediate delivery; permission is the real iOS authorization prompt.
internal sealed class NativeNotifications : INotifications
{
    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public async ValueTask<NotificationPermission> PermissionAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync().ConfigureAwait(false);
        return Map(settings.AuthorizationStatus);
    }

    public async ValueTask<NotificationPermission> RequestPermissionAsync()
    {
        // Alert|Sound|Badge mirrors the web default (a visible, audible notification that can also badge).
        await UNUserNotificationCenter.Current
            .RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound |
                                       UNAuthorizationOptions.Badge)
            .ConfigureAwait(false);
        // Re-read for the definitive status (the request tuple only reports the Alert grant).
        return await PermissionAsync().ConfigureAwait(false);
    }

    public async ValueTask ShowAsync(string title, NotificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        options ??= new NotificationOptions();

        // Mirror the JS default: showing without permission is an error callers try/catch, not a silent no-op.
        if (await PermissionAsync().ConfigureAwait(false) != NotificationPermission.Granted)
        {
            throw new InvalidOperationException("Notification permission not granted.");
        }

        var content = new UNMutableNotificationContent { Title = title };
        if (!string.IsNullOrEmpty(options.Body))
        {
            content.Body = options.Body;
        }

        // Silent suppresses sound (iOS has no per-notification vibration toggle); otherwise the default sound.
        content.Sound = options.Silent == true ? null : UNNotificationSound.Default;

        // Web semantics: a new notification with the same Tag replaces the previous one — map Tag to the
        // request identifier (iOS coalesces same-identifier requests). No Tag → a unique id so each shows.
        var id = string.IsNullOrEmpty(options.Tag) ? Guid.NewGuid().ToString("N") : options.Tag;
        // trigger: null delivers immediately.
        var request = UNNotificationRequest.FromIdentifier(id, content, trigger: null);
        await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request).ConfigureAwait(false);
    }

    private static NotificationPermission Map(UNAuthorizationStatus status) => status switch
    {
        UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral
            => NotificationPermission.Granted,
        UNAuthorizationStatus.Denied => NotificationPermission.Denied,
        _ => NotificationPermission.Default
    };
}
