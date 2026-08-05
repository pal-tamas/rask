using System.Reflection.Metadata;
using Rask.Core.Diagnostics;
using Rask.Core.HotReload;

namespace Rask.Native.HotReload;

/// <summary>
///     Announces an applied hot reload to the WebView, for the day one can reach a native app.
/// </summary>
/// <remarks>
///     <para>
///         <b>This does not make native hot reload work, and is not claimed to.</b> Delivering new IL to
///         an app on a device or simulator needs a device-side delta agent that does not exist —
///         <c>dotnet watch</c> cannot drive either, which is why <c>rask dev</c> refuses native targets.
///         See #565.
///     </para>
///     <para>
///         What it does is finish the half that <i>is</i> free. <c>NativeLiveSession</c> derives from
///         <c>LiveSessionBase</c>, so it already registers with the coordinator and already repaints
///         through <c>INativeWebView.ApplyRenderAsync</c>; the indicator was the only missing piece, and
///         it is a few lines now that the pill is a shared module. If a delta channel ever lands, the
///         framework side needs no further work.
///     </para>
///     <para>
///         Gated on <see cref="MetadataUpdater.IsSupported" />, so a device build — where it can never
///         fire — subscribes to nothing and pays nothing.
///     </para>
/// </remarks>
internal static class NativeHotReloadBridge
{
    private const string ShowPill = "window.__raskHotReloadPill && window.__raskHotReloadPill()";

    private static readonly Lock Gate = new();
    private static INativeWebView? _webView;
    private static bool _subscribed;

    /// <summary>
    ///     Points the indicator at <paramref name="webView" />. Idempotent, and re-pointable: a host that
    ///     recreates its session must not stack handlers, and the newest WebView is the live one.
    /// </summary>
    internal static void Attach(INativeWebView webView)
    {
        if (!MetadataUpdater.IsSupported)
        {
            return;
        }

        lock (Gate)
        {
            _webView = webView;
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            RaskHotReload.Applied += OnApplied;
        }
    }

    private static void OnApplied()
    {
        var webView = Volatile.Read(ref _webView);
        if (webView is null)
        {
            return;
        }

        try
        {
            // Fire-and-forget: the coordinator's completion path must not block on a WebView round
            // trip, and the indicator is cosmetic. Faults are observed below rather than left to the
            // unobserved-exception handler.
            _ = ShowAsync(webView);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private static async Task ShowAsync(INativeWebView webView)
    {
        try
        {
            await webView.EvaluateJavaScriptAsync(ShowPill).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private static void Report(Exception ex) =>
        RaskDiagnostics.Report(
            RaskLogLevel.Warning, "Rask.HotReload", "Rask: hot-reload indicator failed", ex);
}
