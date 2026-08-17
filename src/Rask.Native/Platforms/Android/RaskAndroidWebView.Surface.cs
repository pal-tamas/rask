using Rask.Native.Surface;
using AndroidView = Android.Views.View;

namespace Rask.Native;

// The Android INativeSurface backend — the mirror of the iOS one. A NativeScreen paints as a real View tree
// in the same container that holds the WebView and the bars, and the container keeps BOTH content views,
// toggling only which is visible: the WebView's DOM still matches the session's HTML diff baseline while a
// native route shows, and the retained View tree still matches the node baseline while a web route does.
public sealed partial class RaskAndroidWebView : INativeSurface
{
    private NativeSurfaceHost<AndroidView>? _surfaceHost;

    /// <inheritdoc />
    public Func<NativeSurfaceEvent, Task>? OnSurfaceEvent { get; set; }

    /// <inheritdoc />
    public ValueTask ShowWebViewAsync()
    {
        _webView.Post(ShowWebViewContent);
        return default;
    }

    /// <inheritdoc />
    public ValueTask MountAsync(NativeNode root)
    {
        _webView.Post(() =>
        {
            // Built on the UI thread: Android views may only be created and touched there.
            var host = _surfaceHost = new NativeSurfaceHost<AndroidView>(
                new AndroidViewOps(_context, RaiseSurfaceEvent));
            ShowNativeContent(host.Mount(root));
        });
        return default;
    }

    /// <inheritdoc />
    public ValueTask PatchAsync(IReadOnlyList<NativePatch> patches)
    {
        _webView.Post(() =>
        {
            // An empty patch list still means "this frame is native" — the signal to come back from the
            // WebView when the screen's content happens to be unchanged.
            if (_surfaceHost is { IsMounted: true } host)
            {
                host.Apply(patches);
                ShowNativeContent(host.RootView!);
            }
        });
        return default;
    }

    private void RaiseSurfaceEvent(NativeSurfaceEvent surfaceEvent) =>
        _ = OnSurfaceEvent?.Invoke(surfaceEvent);
}
