using System.Runtime.Versioning;
using Android.App;
using Android.Content.PM;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for INotifications — the platform NotificationManager instead of the WebView's
// Notification constructor (android.webkit.WebView doesn't support it). Registered by AndroidPlatform; the
// framework resolves it over the JS default (native-first). Needs POST_NOTIFICATIONS (API 33+) in the manifest
// + a runtime grant. Icon/Badge URLs and RequireInteraction have no native equivalent here and are ignored;
// Silent routes to a no-sound channel. Matches the JS default's contract: a denied permission throws.
internal sealed class NativeNotifications(Activity activity) : INotifications
{
    private const string DefaultChannel = "rask_default";
    private const string SilentChannel = "rask_silent";
    private const int PostNotificationsRequest = 101;
    private const int DefaultId = 1;
    private static int _counter = DefaultId;

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<NotificationPermission> PermissionAsync() => ValueTask.FromResult(CurrentPermission());

    public async ValueTask<NotificationPermission> RequestPermissionAsync()
    {
        // Before API 33 notifications need no runtime permission; already-granted needs no prompt.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || CurrentPermission() == NotificationPermission.Granted)
        {
            return NotificationPermission.Granted;
        }

        var granted = await RequestPostNotificationsAsync().ConfigureAwait(false);
        return granted ? NotificationPermission.Granted : NotificationPermission.Denied;
    }

    public ValueTask ShowAsync(string title, NotificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        options ??= new NotificationOptions();

        // Mirror the JS default: showing without permission is an error callers try/catch, not a silent no-op.
        if (CurrentPermission() != NotificationPermission.Granted)
        {
            throw new InvalidOperationException("Notification permission not granted.");
        }

        var silent = options.Silent == true;
        var channelId = silent ? SilentChannel : DefaultChannel;
        AndroidNotifications.EnsureChannel(activity, channelId, silent ? "Silent" : "Notifications",
            silent ? NotificationImportance.Low : NotificationImportance.Default);

        var notification = AndroidNotifications.Builder(activity, channelId)
            .SetContentTitle(title)!
            .SetContentText(options.Body)!
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)!
            .SetAutoCancel(true)!
            .Build();

        // Web semantics: a same-Tag notification replaces the previous one — key the id off the tag. No tag →
        // a fresh id each time so every notification shows.
        var id = string.IsNullOrEmpty(options.Tag) ? Interlocked.Increment(ref _counter) : DefaultId;
        AndroidNotifications.Manager(activity).Notify(options.Tag, id, notification);
        return default;
    }

    private NotificationPermission CurrentPermission() =>
        !OperatingSystem.IsAndroidVersionAtLeast(33)
        || activity.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted
            ? NotificationPermission.Granted
            : NotificationPermission.Default;

    [SupportedOSPlatform("android33.0")]
    private Task<bool> RequestPostNotificationsAsync() =>
        NativePermissions.RequestAsync(activity, Android.Manifest.Permission.PostNotifications, PostNotificationsRequest);
}
