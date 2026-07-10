using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
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
        Window.RootViewController = new UIViewController { View = webView.View };
        Window.MakeKeyAndVisible();

        // Wire the in-process session BEFORE loading the shell, so the session is ready to receive the
        // client's `ready` handshake and push the first frame. Native + Local mode.
        _ = StartAsync(webView);
        return true;
    }

    private async Task StartAsync(RaskWkWebView webView)
    {
        var host = NativeAppHost.CreateDefault();
        // Native device backend: hand IShare to the iOS OS share sheet (UIActivityViewController), overriding
        // Rask.Native's JS-backed default. Register any native backend on host.Services before RunLocalAsync
        // — the last registration wins. See docs/native.md "Native device backends".
        host.Services.AddSingleton<IShare>(_ => new NativeShare(() => Window?.RootViewController));
        // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
        _app = await host.RunLocalAsync<App>(webView);
        webView.LoadShell();
    }

    public override void WillTerminate(UIApplication application) => _ = _app?.DisposeAsync();
}
