using Android.App;
using Android.Content.PM;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IPermissions — the OS app permission instead of navigator.permissions, for the
// names that HAVE a native backend behind them.
//
// The WebView's Permissions API describes the WEBVIEW's grants, but IGeolocation/INotifications/IClipboard
// resolve to native backends gated by the OS app permission (manifest + runtime grant) — a different system,
// so a WebView answer is about something the caller isn't going to use. This reads the same source those
// backends do: Activity.CheckSelfPermission, exactly what NativeGeolocation.HasPermission checks.
//
// Camera and Microphone are deliberately NOT answered from the OS grant — see QueryAsync. Registered by
// AndroidPlatform.
//
// Named for what it does rather than Native+interface: NativePermissions is already taken by the public
// runtime-request bridge (heads forward OnRequestPermissionsResult to it, and the CLI scaffolds that call),
// so renaming it would break every scaffolded app. The two are companions — this one asks, that one requests.
internal sealed class NativePermissionQuery(Activity activity, IPermissions webView) : IPermissions
{
    public ValueTask<PermissionState> QueryAsync(PermissionName name) => name switch
    {
        PermissionName.Geolocation => new(Check(Android.Manifest.Permission.AccessFineLocation)),

        PermissionName.Notifications => new(NotificationState()),

        // NOT the OS grant. Nothing on a native head consumes CAMERA/RECORD_AUDIO: IMediaDevices is
        // WASM-only, so capture goes through the WebView, which gates it on its OWN permission
        // (WebChromeClient.OnPermissionRequest) rather than on the app's. Answering "granted" because the
        // app holds the OS grant would tell a caller it can capture without prompting when the WebView is
        // the gate it will actually meet. These are also the two names the WebView answers well, so
        // deferring costs nothing.
        PermissionName.Camera or PermissionName.Microphone => webView.QueryAsync(name),

        // ClipboardManager (what the native IClipboard uses) needs no permission to read or write in the
        // foreground, and app-internal storage is never evicted — which is what persistent-storage asks
        // about. Granted is accurate here, not a placeholder for "don't know".
        PermissionName.ClipboardRead or PermissionName.ClipboardWrite => new(PermissionState.Granted),
        PermissionName.PersistentStorage => new(PermissionState.Granted),

        // Faulted rather than thrown, so an unknown value behaves the same on both platforms (iOS's
        // QueryAsync is async and could only fault) and a caller awaiting inside a try catches it either way.
        _ => ValueTask.FromException<PermissionState>(
            new ArgumentOutOfRangeException(nameof(name), name, "Unknown permission name."))
    };

    // POST_NOTIFICATIONS only exists on API 33+. Below it there is no runtime permission — but the user can
    // still switch the app's notifications off in Settings, and then a posted notification simply never
    // appears. AreNotificationsEnabled covers both eras, so this reports on what the user would actually see
    // rather than on whether a permission string is held.
    private PermissionState NotificationState()
    {
        if (!AndroidNotifications.Manager(activity).AreNotificationsEnabled())
        {
            return PermissionState.Denied;
        }

        return !OperatingSystem.IsAndroidVersionAtLeast(33)
            ? PermissionState.Granted
            : Check(Android.Manifest.Permission.PostNotifications);
    }

    // CheckSelfPermission reports Granted or Denied and nothing else — it cannot separate "never asked" from
    // "denied permanently", and ShouldShowRequestPermissionRationale doesn't close the gap either (it is
    // false BOTH before the first ask and after "Don't allow" twice). So anything not granted reports Prompt,
    // which here means exactly "not granted, asking is still worth a try" — NOT a promise that a dialog will
    // appear. Reporting Denied instead would tell callers a first-run permission is permanently blocked,
    // which is wrong more often. iOS has the real tri-state and reports it.
    private PermissionState Check(string permission) =>
        activity.CheckSelfPermission(permission) == Permission.Granted
            ? PermissionState.Granted
            : PermissionState.Prompt;
}
