using Android.App;
using Android.OS;
using Rask.Native;

namespace Company.RaskNative;

// Native + Server mode: a thin native shell over a REMOTE Rask Server. The WebView machinery — the native
// capability bridge (so the remote page's Shareable / IShare reach the device's native backends), the
// trusted-origin gating, and the off-origin-to-system-browser diversion — lives in Rask.Native's
// RaskServerWebView. This head just points it at your server and supplies the native share backend.
[Activity(Label = "Rask App", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class ServerActivity : Activity
{
    // Your remote Rask Server. (Android emulator → host machine is http://10.0.2.2:<port>; a real
    // deployment is https. For http during development, allow cleartext in AndroidManifest.xml.)
    private static readonly Uri ServerOrigin = new("https://app.example.com/");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(RaskServerWebView.Create(this, ServerOrigin, new NativeShare(this)));
    }
}
