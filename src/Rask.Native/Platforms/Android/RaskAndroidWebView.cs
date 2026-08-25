using System.Text;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Webkit;
using Java.Interop;

using Rask.Chrome;

namespace Rask.Native;

/// <summary>
///     The Android <see cref="INativeWebView" /> — an <c>android.webkit.WebView</c> served from a real https
///     origin (so <c>localStorage</c> / <c>crypto.subtle</c> / secure-context device APIs work). Its
///     <c>ShouldInterceptRequest</c> serves the whole app origin through <see cref="NativeOriginAssets" />:
///     the boot shell + client, scoped CSS/JS, and your bundled static files (via a reader, default
///     <see cref="AndroidBundledAssets" />). Create it, <c>SetContentView(webView.View)</c>, wire the session
///     (<c>RunLocalAsync</c>), then <see cref="LoadShell" />.
/// </summary>
public sealed partial class RaskAndroidWebView : INativeWebView, INativeChrome
{
    /// <summary>The default app origin the shell + client + assets are served from.</summary>
    public const string DefaultOrigin = "https://appassets.rask/";

    private readonly string _origin;
    private readonly Func<string, byte[]?> _readStaticFile;
    private readonly WebView _webView;
    private readonly Context _context;

    /// <summary>The Android view to hand to <c>SetContentView</c>.</summary>
    public Android.Views.View View => _webView;

    /// <inheritdoc />
    public IReadOnlyList<string> Capabilities { get; set; } = [];

    /// <inheritdoc />
    public Func<byte[], Task>? OnMessage { get; set; }

    /// <param name="context">The hosting activity/context.</param>
    /// <param name="origin">The app origin (default <see cref="DefaultOrigin" />).</param>
    /// <param name="staticFileReader">
    ///     Reads a bundled static file by its origin-relative key; defaults to <see cref="AndroidBundledAssets.Read" />
    ///     (the app's Android assets). See <see cref="NativeOriginAssets.Resolve" />.
    /// </param>
    public RaskAndroidWebView(Context context, string origin = DefaultOrigin, Func<string, byte[]?>? staticFileReader = null)
    {
        _origin = origin;
        _readStaticFile = staticFileReader ?? AndroidBundledAssets.Read;
        _context = context;
        _webView = new WebView(context);
        var settings = _webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;   // localStorage / sessionStorage
        _webView.SetWebViewClient(new ShowcaseWebViewClient(_origin, _readStaticFile));
        _webView.AddJavascriptInterface(new RaskJsBridge(this), "__raskBridge");
    }

    /// <summary>Load the boot shell once the session is wired (call from the Activity).</summary>
    public void LoadShell() => _webView.LoadUrl(_origin + "index.native.html");

    // The origin a Url-mode NativeWebView named, once one has been loaded. Null while the app hosts its own
    // markup, and the policy below is inert then.
    private Uri? _remoteOrigin;

    /// <inheritdoc />
    // Announces the shell on every load of the declared origin, so the app on the other end describes its
    // bars for this process to draw rather than rendering them as HTML the user would see twice.
    internal static IDictionary<string, string> NativeShellHeaders { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NativeShellProtocol.ShellHeader] = NativeShellProtocol.NativeShell,
        };

    public ValueTask LoadUrlAsync(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        _remoteOrigin = url;
        _webView.Post(() =>
        {
            // Swapped in rather than configured up front, so an app that never names a Url keeps exactly the
            // client it had. The asset interceptor is still wanted: scoped CSS/JS and the app's own bundled
            // files are served from the app origin regardless of what the page is.
            _webView.SetWebViewClient(new ShowcaseWebViewClient(_origin, _readStaticFile, url, Capabilities));
            _webView.LoadUrl(url.ToString(), NativeShellHeaders);
        });
        return default;
    }

    /// <inheritdoc />
    public ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8)
    {
        var json = Encoding.UTF8.GetString(frameUtf8.Span);
        // Pass the frame JSON to the client as a JS string literal without the reflection-based
        // JsonSerializer.Serialize<T> (IL2026-clean when the app is trimmed).
        Eval("window.__raskNative.applyRender(\"" + JsonEncodedText.Encode(json) + "\")");
        return default;
    }

    /// <inheritdoc />
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
internal sealed class RaskJsBridge(RaskAndroidWebView owner) : Java.Lang.Object
{
    [JavascriptInterface]
    [Export("dispatch")]
    public void Dispatch(string message) => owner.OnJsMessage(message);
}

// Serves the app origin from NativeOriginAssets (shell/client + scoped assets + bundled static files);
// under-origin misses return an empty 200 so the page never hangs, and off-origin requests fall through to
// the real network.
//
// When remoteOrigin is set the app is in Url mode, and this client also carries the policy that mode needs:
// inject the capability bridge for that origin, and keep the WebView on it.
internal sealed class ShowcaseWebViewClient(
    string origin,
    Func<string, byte[]?> readStaticFile,
    Uri? remoteOrigin = null,
    IReadOnlyList<string>? remoteCapabilities = null) : WebViewClient
{
    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (url is null || !url.StartsWith(origin, StringComparison.Ordinal))
        {
            return base.ShouldInterceptRequest(view, request);
        }

        var path = new Uri(url).AbsolutePath;
        if (NativeOriginAssets.Resolve(path, readStaticFile) is { } asset)
        {
            return new WebResourceResponse(asset.ContentType, "UTF-8", new MemoryStream(asset.Body));
        }

        // Under-origin miss (favicon we don't ship, …) — empty 200 so nothing blocks the render.
        return new WebResourceResponse("text/plain", "UTF-8", new MemoryStream([]));
    }

    // The page loaded from a Url gets the capability bridge, so it can reach the device backends this app
    // registered — but only the origin the app actually named.
    public override void OnPageStarted(WebView? view, string? url, Android.Graphics.Bitmap? favicon)
    {
        base.OnPageStarted(view, url, favicon);

        if (remoteOrigin is { } trusted && NativeCapabilities.IsTrustedOrigin(trusted, url))
        {
            view?.EvaluateJavascript(NativeCapabilities.BridgeScript(remoteCapabilities ?? []), null);
        }
    }

    // Keep the WebView on that origin. Anything else opens in the system browser — the grant is to the page
    // you pointed at, and this is what stops it travelling with the user somewhere you did not.
    //
    // This matters more on Android than the equivalent does on iOS: __raskBridge is a @JavascriptInterface
    // bound to the WebView itself, so it is reachable by whatever document the WebView holds.
    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (remoteOrigin is not { } trusted || url is null)
        {
            return base.ShouldOverrideUrlLoading(view, request);
        }

        if (NativeCapabilities.IsTrustedOrigin(trusted, url))
        {
            // A reload or a redirect issues its own request and WebView does not carry our header onto it.
            // Re-issue it once, with the header, so the document comes back describing native chrome instead
            // of painting HTML bars. Loop-free: the replacement request carries the header.
            if (view is not null
                && request!.IsForMainFrame
                && request.RequestHeaders?.ContainsKey(NativeShellProtocol.ShellHeader) != true)
            {
                view.LoadUrl(url, RaskAndroidWebView.NativeShellHeaders);
                return true;
            }

            return base.ShouldOverrideUrlLoading(view, request);
        }

        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        view?.Context?.StartActivity(intent);
        return true;
    }
}

/// <summary>
///     The default Android bundled-asset reader for <see cref="NativeOriginAssets" /> — reads the app's
///     Android assets (add your <c>wwwroot</c> as <c>AndroidAsset</c> at the asset root, and Bootstrap under
///     <c>_content/Rask.Bootstrap/</c>) by origin-relative key, e.g. <c>global.css</c>, <c>data/posts-1.json</c>.
/// </summary>
public static class AndroidBundledAssets
{
    /// <summary>Reads a bundled asset by key, or <see langword="null" /> if it does not exist.</summary>
    public static byte[]? Read(string relativePath)
    {
        var assets = Application.Context.Assets;
        if (assets is null)
        {
            return null;
        }

        try
        {
            using var stream = assets.Open(relativePath);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Java.IO.IOException)
        {
            // Not bundled (FileNotFoundException) or otherwise unreadable (e.g. the key names a directory) —
            // treat as a miss so the interceptor serves its empty-200 fallback rather than throwing.
            return null;
        }
    }
}
