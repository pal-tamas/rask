using CoreFoundation;
using Rask.Native.Surface;
using UIKit;

namespace Rask.Native;

// The iOS INativeSurface backend: paints a pure-native screen as a real UIView tree inside the same container
// that holds the WebView and the bars, so an app can serve one route as HTML and the next fully native.
//
// The container keeps BOTH content views alive and only toggles which is visible. That is the contract in
// INativeSurface, and it is what keeps the session's two diff baselines truthful: the WebView's DOM still
// matches the HTML baseline while a native route is showing, so coming back does not reload the page, and the
// retained UIView tree still matches the node baseline, so going back to native patches instead of remounting.
public sealed partial class RaskWkWebView : INativeSurface
{
    private NativeSurfaceHost<UIView>? _surfaceHost;

    /// <inheritdoc />
    public Func<NativeSurfaceEvent, Task>? OnSurfaceEvent { get; set; }

    /// <inheritdoc />
    public ValueTask ShowWebViewAsync()
    {
        var container = _chromeView ??= new RaskChromeContainerView(View);
        DispatchQueue.MainQueue.DispatchAsync(() => container.ShowWebView());
        return default;
    }

    /// <inheritdoc />
    public ValueTask MountAsync(NativeNode root)
    {
        var container = _chromeView ??= new RaskChromeContainerView(View);
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            // Built on the UI thread: creating a UIView off it is undefined behaviour, and the whole tree is
            // constructed here rather than marshalled piecemeal.
            var host = _surfaceHost = new NativeSurfaceHost<UIView>(new UiKitViewOps(RaiseSurfaceEvent));
            container.ShowNative(host.Mount(root));
        });
        return default;
    }

    /// <inheritdoc />
    public ValueTask PatchAsync(IReadOnlyList<NativePatch> patches)
    {
        var container = _chromeView ??= new RaskChromeContainerView(View);
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            // An empty patch list still means "this frame is native" — it is the signal to come back from the
            // WebView when the screen's content happens to be unchanged.
            if (_surfaceHost is { IsMounted: true } host)
            {
                host.Apply(patches);
                container.ShowNative(host.RootView!);
            }
        });
        return default;
    }

    private void RaiseSurfaceEvent(NativeSurfaceEvent surfaceEvent) =>
        _ = OnSurfaceEvent?.Invoke(surfaceEvent);
}
