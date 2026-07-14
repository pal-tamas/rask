using Android.App;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IBadge. Android has no universal app-icon badge API; launchers surface a count
// from an active notification's number (the notification "dot"). Zero-dep approach (no ShortcutBadger): keep a
// single silent, low-importance badge notification — `SetNumber` for a count, no number for a plain dot;
// ClearAsync cancels it. The exact dot/count rendering is launcher-dependent. Because the count rides a
// notification, this needs POST_NOTIFICATIONS on API 33+ (like NativeNotifications). Registered by
// AndroidPlatform (native-first).
internal sealed class NativeBadge(Activity activity) : IBadge
{
    private const string ChannelId = "rask_badge";
    private const int BadgeId = 0x7A5C; // "rask" — a stable id so updates replace the badge notification.

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask SetAsync(int? count = null)
    {
        AndroidNotifications.EnsureChannel(activity, ChannelId, "App badge", NotificationImportance.Min);
        var builder = AndroidNotifications.Builder(activity, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)!
            .SetOngoing(true)!;

        // Web Badging semantics: a positive number shows that count; null/0 shows a plain dot (no number).
        if (count is > 0)
        {
            builder.SetNumber(count.Value);
        }

        AndroidNotifications.Manager(activity).Notify(BadgeId, builder.Build());
        return default;
    }

    public ValueTask ClearAsync()
    {
        AndroidNotifications.Manager(activity).Cancel(BadgeId);
        return default;
    }
}
