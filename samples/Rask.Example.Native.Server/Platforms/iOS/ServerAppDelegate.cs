using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Rask.Native;
using UIKit;

namespace Rask.Example.Native.Server;

// Native + Server (iOS): a thin native shell over a REMOTE Rask.Example.Server — written as an ordinary
// Rask app whose single page is a NativeWebView pointed at that server.
//
// This is the same head shape as the in-process showcase: one RaskWkWebView, one NativeAppHost, one
// RunLocalAsync. What makes it the Server model is the Url on the component, not a different platform
// class — the bars are still declared in C# and still render as real UIKit chrome around the remote page,
// and the capability bridge (trusted-origin gating, off-origin links to Safari) is applied by the head
// because the component named an address.
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    // The iOS simulator reaches the host machine at localhost. Publish + run Rask.Example.Server first
    // (a real deployment is https). See docs/native.md.
    private static readonly Uri ServerUrl = new("http://localhost:5080/");

    public override UIWindow? Window { get; set; }
    private NativeApp? _app;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);

        var webView = new RaskWkWebView();
        // ChromeView, not the bare WebView, so the app's NativeHeaderBar becomes a real UINavigationBar
        // around the remote page.
        Window.RootViewController = new UIViewController { View = webView.ChromeView };
        Window.MakeKeyAndVisible();

        _ = StartAsync(webView);
        return true;
    }

    private async Task StartAsync(RaskWkWebView webView)
    {
        var host = NativeAppHost.CreateDefault();
        // UsePlatform, not a hand-picked service or two. The remote page reaches these through the
        // capability bridge, and the bridge advertises exactly what the platform module registered — so
        // registering IShare by hand would give the page ONE backend and leave the other fourteen falling
        // back to the WebView's JS, which is the gap #778 exists to close.
        host.UsePlatform(new ApplePlatform(() => Window?.RootViewController));
        host.Services.AddSingleton<INativeChrome>(webView);
        host.Services.AddSingleton(new ServerOrigin(ServerUrl));

        _app = await host.RunLocalAsync<ServerShellApp>(webView);

        // The boot shell still loads first, even though this app hosts no markup: RunLocalAsync defers the
        // first render to the client's `ready` handshake, and the client lives in the shell. That first
        // frame is what carries the Url, so the WebView navigates away from the shell immediately after.
        webView.LoadShell();
    }

    public override void WillTerminate(UIApplication application) => _ = _app?.DisposeAsync();
}
