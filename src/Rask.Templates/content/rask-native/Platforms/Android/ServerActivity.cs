using System.Text;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Webkit;
using Java.Interop;
using Rask.Client.Browser;
using Rask.Native;

namespace Company.RaskNative;

// Native + Server mode: a thin native shell over a REMOTE Rask Server. The C# app runs on the server; this
// WebView just loads it. There is no in-process session — instead the head injects the native
// device-capability bridge (NativeCapabilities) so the remote page's Shareable / IShare reach the device's
// native backends (the OS share sheet) — the "server superpower".
//
// SECURITY: the bridge is exposed only to your trusted origin. The WebViewClient keeps the WebView ON that
// origin — every off-origin navigation opens in the system browser — so no other page can reach native.
[Activity(Label = "Rask App", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class ServerActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Your remote Rask Server. (Android emulator → host machine is http://10.0.2.2:<port>; a real
        // deployment is https. For http during development, allow cleartext in AndroidManifest.xml.)
        NativeServerShell shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));

        var webView = new WebView(this);
        var settings = webView.Settings!;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;

        // The native backend the bridge routes "share" to (reused verbatim from Native + Local).
        IShare share = new NativeShare(this);

        // JS → native: window.__raskBridge.dispatch (used by NativeCapabilities.BridgeScript's send()).
        webView.AddJavascriptInterface(new RaskServerBridge(share), "__raskBridge");
        webView.SetWebViewClient(new RaskServerWebViewClient(shell.ServerBaseUrl));

        SetContentView(webView);
        webView.LoadUrl(shell.ServerBaseUrl.ToString());
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
        if (view is not null && IsTrusted(url))
        {
            view.EvaluateJavascript(NativeCapabilities.BridgeScript, null);
        }
    }

    // Keep the WebView on the trusted origin; open everything else in the system browser so the bridge is
    // never exposed to an untrusted page.
    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (url is not null && !IsTrusted(url) && view?.Context is { } ctx)
        {
            ctx.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url)));
            return true;
        }

        return false;
    }

    private bool IsTrusted(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        string.Equals(u.Host, origin.Host, StringComparison.OrdinalIgnoreCase);
}
