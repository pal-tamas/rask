using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Webkit;
using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Native.Data;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Native;
using Rask.Native.Surface;
using Rask.SQLite;

namespace Rask.Example.Native;

// Native + Local: the showcase runs IN-PROCESS. Mirrors Rask.Example.Server's Program.cs and
// Rask.Example.Wasm's Program.cs — register the shared demo services and mount the shared App. The WebView
// bridge (RaskAndroidWebView), the native share backend (NativeShare) and the bundled-asset reader all come
// from Rask.Native; this head is just the entry point that composes them.
[Activity(Label = "Rask Showcase", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class MainActivity : Activity
{
    private NativeApp? _app;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Make the WebView inspectable so the Appium on-device E2E can attach to its WEBVIEW context. This
        // is a showcase, not a shipping app; a real app would gate this on Debug (or omit it).
        WebView.SetWebContentsDebuggingEnabled(true);

        // NativeNotifications (wired by AndroidPlatform) needs the POST_NOTIFICATIONS runtime grant on API 33+
        // — request it up front so a later ShowAsync posts (declared in AndroidManifest.xml).
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 101);
        }

        var webView = new RaskAndroidWebView(this);
        // Use ChromeView (the container with the native header/footer bars) instead of the bare WebView, so the
        // NativeShowcaseApp's NativeHeader/NativeFooter project onto real top/bottom bars.
        SetContentView(webView.ChromeView);

        // Wire the in-process session BEFORE loading the shell so it's ready for the client's `ready`
        // handshake and can push the first frame.
        _ = StartAsync(webView);
    }

    private async Task StartAsync(RaskAndroidWebView webView)
    {
        // Mount the shared showcase — the same App + demo services Rask.Example.Server/Wasm mount — onto a
        // NativeAppHost, pointed at THIS WebView's origin so the demo HttpClient's fetches (data/*.json)
        // resolve against the same secure origin the shell + assets are served from.
        var host = NativeAppHost.CreateDefault();
        host.Services.AddExampleServices(_ => new Uri(RaskAndroidWebView.DefaultOrigin));

        // Native device backends: the Android platform module wires IShare (ACTION_SEND chooser),
        // IGeolocation (LocationManager), IClipboard, IVibration, IWakeLock, and INetworkInfo to their native
        // C# impls. The framework resolves each over the JS-backed default (native-first); everything else
        // falls back to the WebView's JS. Call UsePlatform before RunLocalAsync.
        host.UsePlatform(new AndroidPlatform(this));

        // Native header/footer chrome: the same RaskAndroidWebView instance is the INativeChrome backend.
        host.Services.AddSingleton<INativeChrome>(webView);

        // SPIKE (#775): the same RaskAndroidWebView is also the INativeSurface backend, so a route composing
        // a NativeScreen paints a real android.view.View tree instead of HTML. Mirrors the iOS head.
        host.Services.AddSingleton<INativeSurface>(webView);

        // Serve the demo HttpClient's data/*.json fetches from the app's bundled assets (offline). This
        // AddSingleton overrides the plain-network HttpClient AddExampleServices registered.
        host.Services.AddSingleton(_ =>
            new HttpClient(new NativeAssetHttpHandler(AndroidBundledAssets.Read))
            {
                BaseAddress = new Uri(RaskAndroidWebView.DefaultOrigin)
            });

        // Persist the Todos screen on-device. Rask.SQLite's raw connection factory applies the production
        // pragmas (WAL, foreign_keys, busy_timeout) on every connection; it's reflection-free. The database
        // lives in the app sandbox, and this AddSingleton overrides the in-memory ITodoStore (last
        // registration wins) — so the Todos tab survives an app restart.
        var todoDbPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "rask-todos.db");
        host.Services.AddRaskSqlite($"Data Source={todoDbPath}");
        host.Services.AddSingleton<ITodoStore, SqliteTodoStore>();

        _app = await host.RunLocalAsync<NativeShowcaseApp>(webView);
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
