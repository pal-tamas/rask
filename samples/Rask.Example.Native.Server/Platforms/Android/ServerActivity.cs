using Android.App;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Native;

namespace Rask.Example.Native.Server;

// Native + Server (Android): a thin native shell over a REMOTE Rask.Example.Server — written as an ordinary
// Rask app whose single page is a NativeWebView pointed at that server.
//
// The mirror of the iOS head, and the same shape as the in-process showcase: one RaskAndroidWebView, one
// NativeAppHost, one RunLocalAsync. What makes it the Server model is the Url on the component, not a
// different platform class — the bars still render as real Android chrome around the remote page, and the
// capability bridge (trusted-origin gating, off-origin links to the system browser) is applied because the
// component named an address.
[Activity(Label = "Rask Showcase (Server)", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class ServerActivity : Activity
{
    // The Android emulator reaches the host machine at 10.0.2.2. Publish + run Rask.Example.Server bound to
    // 0.0.0.0:5080 first (a real deployment is https). See docs/native.md.
    private static readonly Uri ServerUrl = new("http://10.0.2.2:5080/");

    private NativeApp? _app;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var webView = new RaskAndroidWebView(this);
        // ChromeView, not the bare WebView, so the app's NativeHeaderBar becomes a real top bar around the
        // remote page.
        SetContentView(webView.ChromeView);

        _ = StartAsync(webView);
    }

    private async Task StartAsync(RaskAndroidWebView webView)
    {
        var host = NativeAppHost.CreateDefault();
        // The remote page reaches the device backends through the capability bridge, so registering them
        // here is what gives that page a real Android share chooser.
        host.Services.AddSingleton<IShare>(_ => new NativeShare(this));
        host.Services.AddSingleton<INativeChrome>(webView);
        host.Services.AddSingleton(new ServerOrigin(ServerUrl));

        // No LoadShell(): this app hosts no markup of its own, so the first frame's Url is what the WebView
        // navigates to.
        _app = await host.RunLocalAsync<ServerShellApp>(webView);
    }

    protected override void OnDestroy()
    {
        _ = _app?.DisposeAsync();
        base.OnDestroy();
    }
}
