using System.Text;
using Foundation;
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
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<string> _capabilities;
    private WKWebView? _webView;

    /// <param name="origin">The trusted remote server origin (the bridge is exposed only here).</param>
    /// <param name="services">
    ///     The app services holding the native backends the bridge routes to. The whole provider rather
    ///     than one interface, because every capability the head registered is reachable now, not just share.
    /// </param>
    /// <param name="capabilities">What to advertise to the page — see <see cref="NativeCapabilityRegistry" />.</param>
    public RaskServerViewController(
        Uri origin, IServiceProvider services, IReadOnlyList<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(capabilities);
        _origin = origin;
        _services = services;
        _capabilities = capabilities;
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
        _webView = webView;
        View = webView;
        webView.LoadRequest(new NSUrlRequest(new NSUrl(_origin.ToString())));
    }

    // JS → native: capability messages (window.__raskNative.invoke).
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        if (message.Body is NSString s)
        {
            _ = NativeCapabilities.TryHandleAsync(
                Encoding.UTF8.GetBytes(s.ToString()),
                _services,
                script =>
                {
                    _webView?.EvaluateJavaScript(new NSString(script), null);
                    return default;
                });
        }
    }

    // Inject the capability bridge only for the trusted origin, once the page commits.
    [Export("webView:didCommitNavigation:")]
    public void DidCommitNavigation(WKWebView webView, WKNavigation navigation)
    {
        if (NativeCapabilities.IsTrustedOrigin(_origin, webView.Url?.AbsoluteString))
        {
            webView.EvaluateJavaScript(new NSString(NativeCapabilities.BridgeScript(_capabilities)), null);
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
