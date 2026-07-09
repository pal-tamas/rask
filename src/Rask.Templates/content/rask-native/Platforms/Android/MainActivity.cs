using Android.App;
using Android.OS;
using Rask.Native;

namespace Company.RaskNative;

[Activity(Label = "Rask App", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class MainActivity : Activity
{
    private NativeApp? _app;
    private RaskAndroidWebView? _webView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _webView = new RaskAndroidWebView(this);
        SetContentView(_webView.View);

        // Wire the in-process session before loading the shell so it's ready for the client's `ready`
        // handshake. Native + Local mode.
        _ = StartAsync(_webView);
    }

    private async Task StartAsync(RaskAndroidWebView webView)
    {
        var host = NativeAppHost.CreateDefault();
        // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
        _app = await host.RunLocalAsync<App>(webView);
        webView.LoadShell();
    }

    protected override void OnDestroy()
    {
        _ = _app?.DisposeAsync();
        base.OnDestroy();
    }
}
