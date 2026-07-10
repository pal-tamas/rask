using Android.App;
using Android.OS;
using Rask.Native;

namespace Rask.Example.Native.Server;

// Native + Server: a thin native shell over a REMOTE Rask.Example.Server. All the WebView machinery — the
// capability bridge, the trusted-origin gating, and the off-origin-to-system-browser diversion — lives in
// Rask.Native's RaskServerWebView; this head just points it at the dev server and supplies the native
// share backend.
[Activity(Label = "Rask Showcase (Server)", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class ServerActivity : Activity
{
    // The Android emulator reaches the host machine at 10.0.2.2. Publish + run Rask.Example.Server bound to
    // 0.0.0.0:5080 first (a real deployment is https). See docs/native.md.
    private static readonly Uri ServerOrigin = new("http://10.0.2.2:5080/");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(RaskServerWebView.Create(this, ServerOrigin, new NativeShare(this)));
    }
}
