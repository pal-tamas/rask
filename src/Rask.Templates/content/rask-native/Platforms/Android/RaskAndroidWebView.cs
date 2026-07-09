using System.Text;
using Android.Content;
using Android.Webkit;
using Java.Interop;
using Rask.Native;

namespace Company.RaskNative;

// The Android INativeWebView: an android.webkit.WebView served from a real https origin (so localStorage /
// crypto.subtle / secure-context device APIs work — a data: origin would break them).
//
//   • .NET → JS:  EvaluateJavascript on the WebView (UI) thread.
//   • JS → .NET:  a @JavascriptInterface (window.__raskBridge.dispatch) → OnMessage.
//   • assets:     a WebViewClient.ShouldInterceptRequest serves index.native.html + rask.native.js for the
//                 https://appassets.rask/ origin from Rask.Native's embedded NativeClientAssets.
public sealed class RaskAndroidWebView : INativeWebView
{
    private const string Origin = "https://appassets.rask/";
    private readonly WebView _webView;

    public Android.Views.View View => _webView;
    public Func<byte[], Task>? OnMessage { get; set; }

    public RaskAndroidWebView(Context context)
    {
        _webView = new WebView(context);
        var settings = _webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;   // localStorage / sessionStorage
        _webView.SetWebViewClient(new RaskWebViewClient());
        _webView.AddJavascriptInterface(new RaskJsBridge(this), "__raskBridge");
    }

    /// <summary>Load the boot shell once the session is wired (called from the Activity).</summary>
    public void LoadShell() => _webView.LoadUrl(Origin + "index.native.html");

    public ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8)
    {
        var json = Encoding.UTF8.GetString(frameUtf8.Span);
        Eval("window.__raskNative.applyRender(" + System.Text.Json.JsonSerializer.Serialize(json) + ")");
        return default;
    }

    public ValueTask EvaluateJavaScriptAsync(string javaScript)
    {
        Eval(javaScript);
        return default;
    }

    internal void OnJsMessage(string message)
    {
        if (OnMessage is { } handler)
        {
            _ = handler(Encoding.UTF8.GetBytes(message));
        }
    }

    // evaluateJavascript must run on the WebView's (UI) thread.
    private void Eval(string javaScript) => _webView.Post(() => _webView.EvaluateJavascript(javaScript, null));
}

// The bridge object exposed to JS as window.__raskBridge — its dispatch(String) is the client's send() path.
internal sealed class RaskJsBridge : Java.Lang.Object
{
    private readonly RaskAndroidWebView _owner;
    public RaskJsBridge(RaskAndroidWebView owner) => _owner = owner;

    [JavascriptInterface]
    [Export("dispatch")]
    public void Dispatch(string message) => _owner.OnJsMessage(message);
}

// Serves the two embedded client assets for the https://appassets.rask/ origin; everything else falls
// through to the default handling.
internal sealed class RaskWebViewClient : WebViewClient
{
    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (url is null || !url.StartsWith("https://appassets.rask/", StringComparison.Ordinal))
        {
            return base.ShouldInterceptRequest(view, request);
        }

        var (body, mime) = url.EndsWith("rask.native.js", StringComparison.Ordinal)
            ? (NativeClientAssets.ClientJs, "text/javascript")
            : (NativeClientAssets.IndexHtml, "text/html");

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new WebResourceResponse(mime, "UTF-8", stream);
    }
}
