using Rask.Core.Browser;
using UIKit;
using UserNotifications;

namespace Rask.Native;

// Native iOS backend for IBadge — the real app-icon badge, instead of navigator.setAppBadge (which targets an
// installed PWA and never touches a native app's icon). Registered by ApplePlatform; native-first over the JS
// default. iOS badges are numeric only — there is no numberless "dot", so the web's dot (SetAsync(null)/0)
// can't be shown and maps to no badge (0); a positive count shows that number.
internal sealed class NativeBadge : IBadge
{
    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask SetAsync(int? count = null) => Apply(count ?? 0);

    public ValueTask ClearAsync() => Apply(0);

    private static ValueTask Apply(int count)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(16))
        {
            // iOS 16+ API; callable off the main thread, ignore the completion.
            UNUserNotificationCenter.Current.SetBadgeCount(count, completionHandler: null);
            return default;
        }

        // iOS 15 fallback: ApplicationIconBadgeNumber (obsoleted in iOS 17, hence the scoped suppression;
        // only reached on iOS 15, where SetBadgeCount doesn't exist). Setter requires the main thread.
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
#pragma warning disable CA1422 // Deprecated on iOS 17; guarded to the iOS 15 path above.
            UIApplication.SharedApplication.ApplicationIconBadgeNumber = count);
#pragma warning restore CA1422
        return default;
    }
}
