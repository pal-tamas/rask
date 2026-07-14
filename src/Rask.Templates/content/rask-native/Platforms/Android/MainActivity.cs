using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;
using Rask.Native;

namespace Company.RaskNative;

[Activity(Label = "Rask App", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class MainActivity : Activity
{
    private NativeApp? _app;
    private RaskAndroidWebView? _webView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // NativeGeolocation (registered below) needs the runtime location grant — request it up front so a
        // later GetCurrentPositionAsync finds it granted (declare ACCESS_FINE_LOCATION in AndroidManifest.xml).
        if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.AccessFineLocation], 100);
        }

        // NativeNotifications (registered below) needs the POST_NOTIFICATIONS runtime grant on API 33+ —
        // request it up front so a later ShowAsync posts (declare POST_NOTIFICATIONS in AndroidManifest.xml).
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 101);
        }

        _webView = new RaskAndroidWebView(this);
        // Host ChromeView (the container that lays the native header/footer bars around the WebView) rather
        // than the bare WebView, so the App's composed NativeHeaderBar/NativeTabBar become real top/bottom bars.
        // The INativeChrome backend is registered below; without it the bars render nothing (the WebView fills
        // the screen), so a native app that navigates via the tab bar should keep that registration.
        SetContentView(_webView.ChromeView);

        // Wire the in-process session before loading the shell so it's ready for the client's `ready`
        // handshake. Native + Local mode.
        _ = StartAsync(_webView);
    }

    private async Task StartAsync(RaskAndroidWebView webView)
    {
        var host = NativeAppHost.CreateDefault();
        // Native device backends: override Rask.Native's JS-backed defaults with the platform APIs. Register
        // any native backend on host.Services before RunLocalAsync — the last registration wins. See
        // docs/native.md "Native device backends".
        host.Services.AddSingleton<IShare>(_ => new NativeShare(this));                  // OS share sheet
        host.Services.AddSingleton<IGeolocation>(_ => new NativeGeolocation(this));       // LocationManager
        host.Services.AddSingleton<INotifications>(_ => new NativeNotifications(this));   // NotificationManager
        host.Services.AddSingleton<IBadge>(_ => new NativeBadge(this));                   // app badge notification
        host.Services.AddSingleton<INativeChrome>(webView);                              // native header/footer bars
        // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
        _app = await host.RunLocalAsync<App>(webView);
        webView.LoadShell();
    }

    // Forward runtime-permission results so NativeNotifications' RequestPermissionAsync can await the grant.
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        NativePermissions.OnResult(requestCode, grantResults);
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnDestroy()
    {
        _ = _app?.DisposeAsync();
        base.OnDestroy();
    }
}
