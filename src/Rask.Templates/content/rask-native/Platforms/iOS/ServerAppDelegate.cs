using System.Text;
using Foundation;
using Rask.Client.Browser;
using Rask.Native;
using UIKit;
using WebKit;

namespace Company.RaskNative;

// Native + Server mode (iOS): a thin native shell over a REMOTE Rask Server. The C# app runs on the server;
// this WKWebView just loads it. There is no in-process session — the head injects the native
// device-capability bridge (NativeCapabilities) so the remote page's Shareable / IShare reach the device's
// native backends (the OS share sheet) — the "server superpower".
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        // Your remote Rask Server (a real deployment is https).
        NativeServerShell shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));

        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new RaskServerViewController(shell.ServerBaseUrl)
        };
        Window.MakeKeyAndVisible();
        return true;
    }
}

// A WKWebView that loads the remote Rask Server and exposes the native capability bridge to its trusted
// origin only. SECURITY: BridgeScript (window.__raskNative) is injected per-navigation only for the trusted
// origin, and off-origin links open in Safari — so no other page can reach native.
public sealed class RaskServerViewController : UIViewController, IWKScriptMessageHandler, IWKNavigationDelegate
{
    private readonly Uri _origin;
    private readonly IShare _share;

    public RaskServerViewController(Uri origin)
    {
        _origin = origin;
        _share = new NativeShare(() => this);   // presents UIActivityViewController from this VC
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        var controller = new WKUserContentController();
        // window.__raskSend forwards to the "rask" script-message handler (BridgeScript's send() uses it).
        controller.AddUserScript(new WKUserScript(
            new NSString("window.__raskSend = function (s) { window.webkit.messageHandlers.rask.postMessage(s); };"),
            WKUserScriptInjectionTime.AtDocumentStart, isForMainFrameOnly: true));
        controller.AddScriptMessageHandler(this, "rask");

        var config = new WKWebViewConfiguration { UserContentController = controller };
        var webView = new WKWebView(UIScreen.MainScreen.Bounds, config)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            NavigationDelegate = this
        };
        View = webView;
        webView.LoadRequest(new NSUrlRequest(new NSUrl(_origin.ToString())));
    }

    // JS → native: capability messages (window.__raskNative.invoke).
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        if (message.Body is NSString s)
        {
            _ = NativeCapabilities.TryHandleAsync(Encoding.UTF8.GetBytes(s.ToString()), _share);
        }
    }

    // Inject the capability bridge only for the trusted origin, once the page commits.
    [Export("webView:didCommitNavigation:")]
    public void DidCommitNavigation(WKWebView webView, WKNavigation navigation)
    {
        if (IsTrusted(webView.Url))
        {
            webView.EvaluateJavaScript(new NSString(NativeCapabilities.BridgeScript), null);
        }
    }

    // Keep the WebView on the trusted origin; open everything else in Safari.
    [Export("webView:decidePolicyForNavigationAction:decisionHandler:")]
    public void DecidePolicyForNavigationAction(
        WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
    {
        var url = navigationAction.Request.Url;
        if (navigationAction.NavigationType == WKNavigationType.LinkActivated && !IsTrusted(url))
        {
            UIApplication.SharedApplication.OpenUrl(url!, new UIApplicationOpenUrlOptions(), null);
            decisionHandler(WKNavigationActionPolicy.Cancel);
            return;
        }

        decisionHandler(WKNavigationActionPolicy.Allow);
    }

    private bool IsTrusted(NSUrl? url) =>
        url?.Host is { } host && string.Equals(host, _origin.Host, StringComparison.OrdinalIgnoreCase);
}
