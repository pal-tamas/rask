using System.Text;
using System.Text.Json;
using CoreFoundation;
using Foundation;
using Rask.Native;
using UIKit;
using WebKit;

namespace Company.RaskNative;

// The iOS INativeWebView: a WKWebView served from a real app-scheme origin (so localStorage /
// crypto.subtle / secure-context device APIs work — an opaque LoadHtmlString origin would break them).
//
//   • .NET → JS:  EvaluateJavaScript on the main thread (WebView JS is UI-thread-affine).
//   • JS → .NET:  a WKScriptMessageHandler ("rask") → OnMessage. An injected window.__raskSend forwards
//                 the client's JSON messages to it.
//   • assets:     a WKUrlSchemeHandler serves index.native.html + rask.native.js from raskapp://local/.
public sealed class RaskWkWebView : NSObject, INativeWebView, IWKScriptMessageHandler
{
    private const string Scheme = "raskapp";
    private const string Origin = "raskapp://local/";

    public WKWebView View { get; }
    public Func<byte[], Task>? OnMessage { get; set; }

    public RaskWkWebView()
    {
        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(new RaskSchemeHandler(), Scheme);

        var controller = new WKUserContentController();
        // Install window.__raskSend at document start so the spliced client's send() reaches native.
        controller.AddUserScript(new WKUserScript(
            new NSString("window.__raskSend = function (s) { window.webkit.messageHandlers.rask.postMessage(s); };"),
            WKUserScriptInjectionTime.AtDocumentStart, isForMainFrameOnly: true));
        controller.AddScriptMessageHandler(this, "rask");
        config.UserContentController = controller;

        // Size to the screen with flexible autoresizing so the WebView fills the window (and follows
        // rotation). Assigning a WKWebView built with an empty frame straight to a view controller's View
        // leaves it at a default size, painting the app content into a small box on a black screen.
        View = new WKWebView(UIScreen.MainScreen.Bounds, config)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
        };
    }

    /// <summary>Load the boot shell once the session is wired (called from the AppDelegate).</summary>
    public void LoadShell() =>
        View.LoadRequest(new NSUrlRequest(new NSUrl(Origin + "index.native.html")));

    public ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8)
    {
        var json = Encoding.UTF8.GetString(frameUtf8.Span);
        Eval("window.__raskNative.applyRender(" + JsonSerializer.Serialize(json) + ")");
        return default;
    }

    public ValueTask EvaluateJavaScriptAsync(string javaScript)
    {
        Eval(javaScript);
        return default;
    }

    // WKScriptMessage.Body is the NSString the page posted via window.__raskSend.
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        if (message.Body is NSString s && OnMessage is { } handler)
        {
            _ = handler(Encoding.UTF8.GetBytes(s.ToString()));
        }
    }

    private void Eval(string javaScript) =>
        DispatchQueue.MainQueue.DispatchAsync(() => View.EvaluateJavaScript(new NSString(javaScript), null));
}

// Serves the two embedded client assets (from Rask.Native's NativeClientAssets) for the raskapp:// origin.
file sealed class RaskSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var path = urlSchemeTask.Request.Url?.Path ?? "/";
        var (body, mime) = path.EndsWith("rask.native.js", StringComparison.Ordinal)
            ? (NativeClientAssets.ClientJs, "text/javascript")
            : (NativeClientAssets.IndexHtml, "text/html");

        var data = NSData.FromString(body, NSStringEncoding.UTF8);
        var response = new NSUrlResponse(urlSchemeTask.Request.Url!, mime, (nint)data.Length, "utf-8");
        urlSchemeTask.DidReceiveResponse(response);
        urlSchemeTask.DidReceiveData(data);
        urlSchemeTask.DidFinish();
    }

    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }
}
