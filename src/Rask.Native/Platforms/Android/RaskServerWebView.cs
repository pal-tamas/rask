using System.Text;
using Android.Content;
using Android.Graphics;
using Android.Webkit;
using Java.Interop;
using Rask.Client.Browser;

namespace Rask.Native;

/// <summary>
///     Builds the Native + Server WebView: a WebView pointed at a remote Rask Server that exposes the native
///     capability bridge (<see cref="NativeCapabilities.BridgeScript" />) to its <b>trusted origin only</b>,
///     so the remote page's <c>Shareable</c> / <c>IShare</c> reach the device's native backends. Off-origin
///     navigations open in the system browser, so no other page can reach native.
/// </summary>
public static class RaskServerWebView
{
    /// <summary>Create a configured Native + Server WebView. Hand the result to <c>SetContentView</c>.</summary>
    /// <param name="context">The hosting activity/context.</param>
    /// <param name="origin">The trusted remote server origin (the bridge is exposed only here).</param>
    /// <param name="share">The native share backend the bridge routes <c>share</c> to.</param>
    public static WebView Create(Context context, Uri origin, IShare share)
    {
        ArgumentNullException.ThrowIfNull(share);
        // Validates the origin is absolute (same contract a Native + Server head's ConnectToServer uses).
        var serverBaseUrl = NativeAppHost.ConnectToServer(origin).ServerBaseUrl;

        var webView = new WebView(context);
        var settings = webView.Settings!;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;

        // JS → native: window.__raskBridge.dispatch (used by NativeCapabilities.BridgeScript's send()).
        webView.AddJavascriptInterface(new RaskServerBridge(share), "__raskBridge");
        webView.SetWebViewClient(new RaskServerWebViewClient(serverBaseUrl));
        webView.LoadUrl(serverBaseUrl.ToString());
        return webView;
    }
}

// Routes a WebView → native message to the shared capability dispatcher with the head's native IShare.
// Only { type:"capability" } envelopes are honoured; the WebViewClient guarantees only the trusted origin
// can reach this interface.
internal sealed class RaskServerBridge(IShare share) : Java.Lang.Object
{
    [JavascriptInterface]
    [Export("dispatch")]
    public void Dispatch(string message) =>
        _ = NativeCapabilities.TryHandleAsync(Encoding.UTF8.GetBytes(message), share);
}

internal sealed class RaskServerWebViewClient(Uri origin) : WebViewClient
{
    // Inject the capability bridge (window.__raskNative) only for the trusted origin, as each page commits.
    public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
    {
        if (view is not null && NativeCapabilities.IsTrustedOrigin(origin, url))
        {
            view.EvaluateJavascript(NativeCapabilities.BridgeScript, null);
        }
    }

    // Keep the WebView on the trusted origin; open everything else in the system browser so the bridge is
    // never exposed to an untrusted page.
    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (url is not null && !NativeCapabilities.IsTrustedOrigin(origin, url) && view?.Context is { } ctx)
        {
            ctx.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url)));
            return true;
        }

        return false;
    }
}
