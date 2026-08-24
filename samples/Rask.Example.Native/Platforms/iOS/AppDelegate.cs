using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Native.Data;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Native;
using Rask.Native.Surface;
using Rask.SQLite;
using UIKit;

namespace Rask.Example.Native;

// Native + Local (iOS): the showcase runs IN-PROCESS. Mirrors Rask.Example.Server/Wasm Program.cs — the
// shared demo services + shared App — onto a NativeAppHost behind a WKWebView.
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }
    private NativeApp? _app;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);

        var webView = new RaskWkWebView();
        // Make the WebView inspectable so the Appium on-device E2E can attach to its WEBVIEW context.
        // This is a showcase, not a shipping app; a real app would gate this on Debug (or omit it). (iOS 16.4+.)
        if (OperatingSystem.IsIOSVersionAtLeast(16, 4))
        {
            webView.View.Inspectable = true;
        }

        // Use ChromeView (the container with the native header/footer bars) instead of the bare WebView, so
        // the NativeShowcaseApp's NativeHeader/NativeFooter project onto real UINavigationBar/UITabBar.
        Window.RootViewController = new UIViewController { View = webView.ChromeView };
        Window.MakeKeyAndVisible();

        // Wire the in-process session BEFORE loading the shell so it's ready for the client's `ready`
        // handshake and can push the first frame.
        _ = StartAsync(webView);
        return true;
    }

    private async Task StartAsync(RaskWkWebView webView)
    {
        // Mount the shared showcase — the same App + demo services Rask.Example.Server/Wasm mount — onto a
        // NativeAppHost, pointed at THIS WebView's origin so the demo HttpClient's fetches (data/*.json)
        // resolve against the same secure origin the shell + assets are served from.
        var host = NativeAppHost.CreateDefault();
        host.Services.AddExampleServices(_ => new Uri(RaskWkWebView.DefaultOrigin));

        // Native device backends: the iOS platform module wires IShare (system share sheet), IGeolocation
        // (CoreLocation), IClipboard, IVibration, IWakeLock, and INetworkInfo to their native C# impls. The
        // framework resolves each over the JS-backed default (native-first); everything else falls back to
        // the WebView's JS. Call UsePlatform before RunLocalAsync.
        host.UsePlatform(new ApplePlatform(() => Window?.RootViewController));

        // Native header/footer chrome: the same RaskWkWebView instance is the INativeChrome backend, so the
        // NativeShowcaseApp's NativeHeader/NativeFooter drive its UINavigationBar/UITabBar.
        host.Services.AddSingleton<INativeChrome>(webView);

        // SPIKE (#775): the same RaskWkWebView is also the INativeSurface backend, so a route composing a
        // NativeScreen paints a real UIView tree instead of HTML. With this line absent the native family is
        // inert and every frame goes through the WebView, which is why nothing had exercised it before.
        host.Services.AddSingleton<INativeSurface>(webView);

        // Serve the demo HttpClient's data/*.json fetches from the app's bundled assets (offline). This
        // AddSingleton overrides the plain-network HttpClient AddExampleServices registered.
        host.Services.AddSingleton(_ =>
            new HttpClient(new NativeAssetHttpHandler(IosBundledAssets.Read))
            {
                BaseAddress = new Uri(RaskWkWebView.DefaultOrigin)
            });

        // Persist the Todos screen on-device. Rask.SQLite's raw connection factory applies the production
        // pragmas (WAL, foreign_keys, busy_timeout) on every connection; it's reflection-free, so it's safe
        // under iOS full-AOT. The database lives in the app sandbox, and this AddSingleton overrides the
        // in-memory ITodoStore (last registration wins) — so the Todos tab survives an app restart.
        var todoDbPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "rask-todos.db");
        host.Services.AddRaskSqlite($"Data Source={todoDbPath}");
        host.Services.AddSingleton<ITodoStore, SqliteTodoStore>();

        _app = await host.RunLocalAsync<NativeShowcaseApp>(webView);
        webView.LoadShell();
    }

    public override void WillTerminate(UIApplication application) => _ = _app?.DisposeAsync();
}
