using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;
using Rask.Native;
using UIKit;

namespace Company.RaskNative;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }
    private NativeApp? _app;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);

        var webView = new RaskWkWebView();
        // Host ChromeView (the container that lays the native header/footer bars around the WebView) rather
        // than the bare WebView, so the App's composed NativeHeaderBar/NativeTabBar become a real
        // UINavigationBar/UITabBar. The INativeChrome backend is registered below; without it the bars render
        // nothing (the WebView fills the screen), so a native app that navigates via the tab bar should keep it.
        Window.RootViewController = new UIViewController { View = webView.ChromeView };
        Window.MakeKeyAndVisible();

        // Wire the in-process session BEFORE loading the shell, so the session is ready to receive the
        // client's `ready` handshake and push the first frame. Native + Local mode.
        _ = StartAsync(webView);
        return true;
    }

    private async Task StartAsync(RaskWkWebView webView)
    {
        var host = NativeAppHost.CreateDefault();
        // Native device backends: override Rask.Native's JS-backed defaults with the platform APIs. Register
        // any native backend on host.Services before RunLocalAsync — the last registration wins. See
        // docs/native.md "Native device backends".
        host.Services.AddSingleton<IShare>(_ => new NativeShare(() => Window?.RootViewController));  // share sheet
        host.Services.AddSingleton<IGeolocation>(_ => new NativeGeolocation());                     // CoreLocation
        host.Services.AddSingleton<INotifications>(_ => new NativeNotifications());                 // UNUserNotificationCenter
        host.Services.AddSingleton<IBadge>(_ => new NativeBadge());                                 // app-icon badge
        host.Services.AddSingleton<INativeChrome>(webView);                                         // native header/footer bars
        // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
        _app = await host.RunLocalAsync<App>(webView);
        webView.LoadShell();
    }

    public override void WillTerminate(UIApplication application) => _ = _app?.DisposeAsync();
}
