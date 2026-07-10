using Foundation;
using Rask.Native;
using UIKit;

namespace Rask.Example.Native.Server;

// Native + Server (iOS): a thin native shell over a REMOTE Rask.Example.Server. The WebView machinery — the
// capability bridge, the trusted-origin gating, and the off-origin-to-Safari diversion — lives in
// Rask.Native's RaskServerViewController; this head just points it at the dev server and supplies the
// native share backend.
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    // The iOS simulator reaches the host machine at localhost. Publish + run Rask.Example.Server first
    // (a real deployment is https). See docs/native.md.
    private static readonly Uri ServerOrigin = new("http://localhost:5080/");

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var shell = NativeAppHost.ConnectToServer(ServerOrigin);
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new RaskServerViewController(
                shell.ServerBaseUrl, new NativeShare(() => Window?.RootViewController))
        };
        Window.MakeKeyAndVisible();
        return true;
    }
}
