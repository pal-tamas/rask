using CoreLocation;
using Rask.Core.Browser;
using UIKit;
using UserNotifications;

namespace Rask.Native;

// Native iOS backend for IPermissions — the OS authorization status instead of navigator.permissions, for
// the names that HAVE a native backend behind them.
//
// Two reasons this exists. (1) WKWebView's Permissions API answers for almost nothing: query() throws
// NotSupportedError for "geolocation"/"notifications" and TypeError for the clipboard/persistent-storage
// names — verified against WKWebView, which answers only "camera" and "microphone" — so five of
// IPermissions' seven typed names faulted the awaited task on the native shell, including the two its own
// docs recommend pairing it with. (2) Even where it answers, it answers about the wrong system:
// IGeolocation/INotifications resolve to native backends gated by the OS app permission (Info.plist + the
// system prompt), which navigator.permissions cannot see. Registered by ApplePlatform.
//
// Named for what it does rather than Native+interface, because Android's public NativePermissions (the
// runtime-request bridge that heads forward OnRequestPermissionsResult to) already owns that name and is
// part of the scaffolded head's API. Kept identical on both platforms so the pair reads as one thing.
internal sealed class NativePermissionQuery(IPermissions webView) : IPermissions
{
    // One manager for the backend's lifetime. IPermissions is a singleton and is meant to be asked before
    // every gated action, so allocating a CLLocationManager per query would leave one NSObject peer (and its
    // CoreLocation registration) behind on each call.
    private CLLocationManager? _location;

    public async ValueTask<PermissionState> QueryAsync(PermissionName name) => name switch
    {
        PermissionName.Geolocation =>
            await MainThreadAsync(() => Map((_location ??= new CLLocationManager()).AuthorizationStatus))
                .ConfigureAwait(false),

        PermissionName.Notifications => Map((await UNUserNotificationCenter.Current
            .GetNotificationSettingsAsync().ConfigureAwait(false)).AuthorizationStatus),

        // NOT AVCaptureDevice. Nothing on a native head consumes the app's camera/mic grant: IMediaDevices
        // is WASM-only, so capture goes through WKWebView, which gates it on its OWN per-origin permission
        // on top of the app's. Answering "granted" because the app holds the OS grant would tell a caller it
        // can capture without prompting when the WebView is the gate it will actually meet. These are also
        // the only two names WKWebView answers correctly, so deferring to it costs nothing and is the more
        // accurate source for exactly these.
        PermissionName.Camera or PermissionName.Microphone =>
            await webView.QueryAsync(name).ConfigureAwait(false),

        // Since iOS 16 a programmatic UIPasteboard read (what the native IClipboard does — see
        // NativeClipboard) raises the system "Allow Paste?" alert unless the user has already allowed that
        // source, so a read really can prompt. Writing never does, and app-sandbox storage is never evicted,
        // which is exactly what persistent-storage asks about.
        PermissionName.ClipboardRead => PermissionState.Prompt,
        PermissionName.ClipboardWrite => PermissionState.Granted,
        PermissionName.PersistentStorage => PermissionState.Granted,

        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown permission name.")
    };

    // CLLocationManager wants to be constructed on a thread with a run loop.
    private static Task<PermissionState> MainThreadAsync(Func<PermissionState> read)
    {
        var tcs = new TaskCompletionSource<PermissionState>();
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            try
            {
                tcs.SetResult(read());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private static PermissionState Map(CLAuthorizationStatus status) => status switch
    {
        CLAuthorizationStatus.AuthorizedAlways or CLAuthorizationStatus.AuthorizedWhenInUse => PermissionState.Granted,
        CLAuthorizationStatus.NotDetermined => PermissionState.Prompt,
        // Restricted (parental controls / MDM) can't be granted by prompting, so it reads as Denied.
        _ => PermissionState.Denied
    };

    private static PermissionState Map(UNAuthorizationStatus status) => status switch
    {
        UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral
            => PermissionState.Granted,
        UNAuthorizationStatus.NotDetermined => PermissionState.Prompt,
        _ => PermissionState.Denied
    };
}
