using System.Text;
using System.Text.Json;
using CoreFoundation;
using Foundation;
using UIKit;
using WebKit;

namespace Rask.Native;

/// <summary>
///     The iOS <see cref="INativeWebView" /> — a <c>WKWebView</c> served from a real app-scheme origin (so
///     <c>localStorage</c> / <c>crypto.subtle</c> / secure-context device APIs work). Its
///     <c>WKUrlSchemeHandler</c> serves the whole app origin through <see cref="NativeOriginAssets" />: the
///     boot shell + client, scoped CSS/JS, and your bundled static files (via a reader, default
///     <see cref="IosBundledAssets" />). Assign <see cref="View" /> to a view controller, wire the session
///     (<c>RunLocalAsync</c>), then <see cref="LoadShell" />.
/// </summary>
public sealed partial class RaskWkWebView : NSObject, INativeWebView, IWKScriptMessageHandler, INativeChrome
{
    /// <summary>The default custom scheme + app origin the shell + client + assets are served from.</summary>
    public const string DefaultScheme = "raskapp";

    /// <summary>The default app origin (<c>raskapp://local/</c>).</summary>
    public const string DefaultOrigin = "raskapp://local/";

    private readonly string _origin;

    /// <summary>The <c>WKWebView</c> to assign to a view controller's <c>View</c>.</summary>
    public WKWebView View { get; }

    /// <inheritdoc />
    public Func<byte[], Task>? OnMessage { get; set; }

    /// <param name="origin">The app origin (default <see cref="DefaultOrigin" />).</param>
    /// <param name="scheme">The custom scheme the origin uses (default <see cref="DefaultScheme" />).</param>
    /// <param name="staticFileReader">
    ///     Reads a bundled static file by its origin-relative key; defaults to <see cref="IosBundledAssets.Read" />
    ///     (a <c>wwwroot</c> folder in the app bundle). See <see cref="NativeOriginAssets.Resolve" />.
    /// </param>
    public RaskWkWebView(string origin = DefaultOrigin, string scheme = DefaultScheme, Func<string, byte[]?>? staticFileReader = null)
    {
        _origin = origin;
        var reader = staticFileReader ?? IosBundledAssets.Read;
        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(new SchemeHandler(reader), scheme);

        var controller = new WKUserContentController();
        // Install window.__raskSend at document start so the spliced client's send() reaches native.
        controller.AddUserScript(new WKUserScript(
            new NSString("window.__raskSend = function (s) { window.webkit.messageHandlers.rask.postMessage(s); };"),
            WKUserScriptInjectionTime.AtDocumentStart, isForMainFrameOnly: true));
        controller.AddScriptMessageHandler(this, "rask");
        config.UserContentController = controller;

        // Size to the screen with flexible autoresizing so the WebView fills the window (and follows
        // rotation). An empty-frame WKWebView assigned straight to a view controller's View stays small.
        View = new WKWebView(UIScreen.MainScreen.Bounds, config)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
        };
    }

    /// <summary>Load the boot shell once the session is wired (call from the AppDelegate).</summary>
    public void LoadShell() =>
        View.LoadRequest(new NSUrlRequest(new NSUrl(_origin + "index.native.html")));

    /// <inheritdoc />
    public ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8)
    {
        var json = Encoding.UTF8.GetString(frameUtf8.Span);
        // Pass the frame JSON to the client as a JS string literal without the reflection-based
        // JsonSerializer.Serialize<T> (IL2026-clean under the iOS app's trimmer).
        Eval("window.__raskNative.applyRender(\"" + JsonEncodedText.Encode(json) + "\")");
        return default;
    }

    /// <inheritdoc />
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

// Serves the app origin from NativeOriginAssets (shell/client + scoped assets + bundled static files);
// under-origin misses return an empty 200 so the page never hangs.
file sealed class SchemeHandler(Func<string, byte[]?> readStaticFile) : NSObject, IWKUrlSchemeHandler
{
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var path = urlSchemeTask.Request.Url?.Path ?? "/";
        var (body, mime) = NativeOriginAssets.Resolve(path, readStaticFile) is { } asset
            ? (asset.Body, asset.ContentType)
            : ([], "text/plain");

        var data = NSData.FromArray(body);
        var response = new NSUrlResponse(urlSchemeTask.Request.Url!, mime, (nint)data.Length, "utf-8");
        urlSchemeTask.DidReceiveResponse(response);
        urlSchemeTask.DidReceiveData(data);
        urlSchemeTask.DidFinish();
    }

    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }
}

/// <summary>
///     The default iOS bundled-asset reader for <see cref="NativeOriginAssets" /> — reads a <c>wwwroot</c>
///     folder in the app bundle (add your <c>wwwroot</c> as <c>BundleResource</c> linked under <c>wwwroot\</c>,
///     Bootstrap under <c>wwwroot\_content\Rask.Bootstrap\</c>) by origin-relative key.
/// </summary>
public static class IosBundledAssets
{
    private static readonly string Root = Path.Combine(NSBundle.MainBundle.BundlePath, "wwwroot");

    /// <summary>Reads a bundled asset by key, or <see langword="null" /> if it does not exist.</summary>
    public static byte[]? Read(string relativePath)
    {
        var file = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }
}
