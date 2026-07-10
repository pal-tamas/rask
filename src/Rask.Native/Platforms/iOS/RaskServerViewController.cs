using System.Text;
using Foundation;
using Rask.Client.Browser;
using UIKit;
using WebKit;

namespace Rask.Native;

/// <summary>
///     The Native + Server view controller: a <c>WKWebView</c> that loads a remote Rask Server and exposes the
///     native capability bridge (<see cref="NativeCapabilities.BridgeScript" />) to its <b>trusted origin
///     only</b>. The bridge is injected per-navigation only for the trusted origin, and any off-origin web
///     navigation opens in Safari — so no other page can reach native. Set it as the window's
///     <c>RootViewController</c>.
/// </summary>
public sealed class RaskServerViewController : UIViewController, IWKScriptMessageHandler, IWKNavigationDelegate
{
    private readonly Uri _origin;
    private readonly IShare _share;

    /// <param name="origin">The trusted remote server origin (the bridge is exposed only here).</param>
    /// <param name="share">The native share backend the bridge routes <c>share</c> to.</param>
    public RaskServerViewController(Uri origin, IShare share)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(share);
        _origin = origin;
        _share = share;
    }

    /// <inheritdoc />
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
        if (NativeCapabilities.IsTrustedOrigin(_origin, webView.Url?.AbsoluteString))
        {
            webView.EvaluateJavaScript(new NSString(NativeCapabilities.BridgeScript), null);
        }
    }

    // Keep the WebView on the trusted origin; open any off-origin WEB navigation (tapped link, server 302,
    // window.location, form POST) in Safari. Non-web schemes (about:blank at startup, …) pass through.
    [Export("webView:decidePolicyForNavigationAction:decisionHandler:")]
    public void DecidePolicyForNavigationAction(
        WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
    {
        var url = navigationAction.Request.Url;
        if (url is { Scheme: "http" or "https" } && !NativeCapabilities.IsTrustedOrigin(_origin, url.AbsoluteString))
        {
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
            decisionHandler(WKNavigationActionPolicy.Cancel);
            return;
        }

        decisionHandler(WKNavigationActionPolicy.Allow);
    }
}
