using Android.App;
using Android.Content;

namespace Rask.Native;

// Shared Android NotificationManager plumbing for NativeNotifications + NativeBadge: the version-gated
// Notification.Builder construction, the system-service lookup, and once-only channel creation, so the two
// backends don't duplicate it (and channels aren't re-created on every post).
internal static class AndroidNotifications
{
    private static readonly HashSet<string> CreatedChannels = [];

    public static NotificationManager Manager(Activity activity) =>
        (NotificationManager)activity.GetSystemService(Context.NotificationService)!;

    public static Notification.Builder Builder(Activity activity, string channelId)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return new Notification.Builder(activity, channelId);
        }

#pragma warning disable CA1422 // Channel-less ctor is obsoleted on Android 26; only reached on API 24-25.
        return new Notification.Builder(activity);
#pragma warning restore CA1422
    }

    // Channels exist on API 26+ and creating one is idempotent, but guard so we call CreateNotificationChannel
    // once per id rather than on every notification.
    public static void EnsureChannel(Activity activity, string id, string name, NotificationImportance importance)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        lock (CreatedChannels)
        {
            if (!CreatedChannels.Add(id))
            {
                return;
            }
        }

        Manager(activity).CreateNotificationChannel(new NotificationChannel(id, name, importance));
    }
}
