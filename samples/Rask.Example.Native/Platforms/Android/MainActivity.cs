using Android.App;
using Android.OS;
using Android.Webkit;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Example.Shared;
using Rask.Native;

namespace Rask.Example.Native;

// Native + Local: the showcase runs IN-PROCESS. Mirrors Rask.Example.Server's Program.cs and
// Rask.Example.Wasm's Program.cs — register the shared demo services and mount the shared App. The WebView
// bridge (RaskAndroidWebView), the native share backend (NativeShare) and the bundled-asset reader all come
// from Rask.Native; this head is just the entry point that composes them.
[Activity(Label = "Rask Showcase", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class MainActivity : Activity
{
    private NativeApp? _app;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Make the WebView inspectable so the Appium on-device E2E can attach to its WEBVIEW context. This
        // is a showcase, not a shipping app; a real app would gate this on Debug (or omit it).
        WebView.SetWebContentsDebuggingEnabled(true);

        var webView = new RaskAndroidWebView(this);
        // Use ChromeView (the container with the native header/footer bars) instead of the bare WebView, so the
        // NativeShowcaseApp's NativeHeader/NativeFooter project onto real top/bottom bars.
        SetContentView(webView.ChromeView);

        // Wire the in-process session BEFORE loading the shell so it's ready for the client's `ready`
        // handshake and can push the first frame.
        _ = StartAsync(webView);
    }

    private async Task StartAsync(RaskAndroidWebView webView)
    {
        // Mount the shared showcase — the same App + demo services Rask.Example.Server/Wasm mount — onto a
        // NativeAppHost, pointed at THIS WebView's origin so the demo HttpClient's fetches (data/*.json)
        // resolve against the same secure origin the shell + assets are served from.
        var host = NativeAppHost.CreateDefault();
        host.Services.AddExampleServices(_ => new Uri(RaskAndroidWebView.DefaultOrigin));

        // Native device backend: hand IShare to the Android OS share sheet (ACTION_SEND chooser). Register
        // native backends on host.Services BEFORE RunLocalAsync — the last registration wins.
        host.Services.AddSingleton<IShare>(_ => new NativeShare(this));

        // Native header/footer chrome: the same RaskAndroidWebView instance is the INativeChrome backend.
        host.Services.AddSingleton<INativeChrome>(webView);

        // Serve the demo HttpClient's data/*.json fetches from the app's bundled assets (offline). This
        // AddSingleton overrides the plain-network HttpClient AddExampleServices registered.
        host.Services.AddSingleton(_ =>
            new HttpClient(new NativeAssetHttpHandler(AndroidBundledAssets.Read))
            {
                BaseAddress = new Uri(RaskAndroidWebView.DefaultOrigin)
            });

        _app = await host.RunLocalAsync<NativeShowcaseApp>(webView);
        webView.LoadShell();
    }

    protected override void OnDestroy()
    {
        _ = _app?.DisposeAsync();
        base.OnDestroy();
    }
}
