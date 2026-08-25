using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Chrome;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Core.Routing;

namespace Rask.Server;

internal sealed partial class LiveSession
{
    private readonly HostedChromePusher _chrome = new();

    /// <inheritdoc />
    /// <remarks>
    ///     Only inside a native shell. Collecting costs the clean-subtree render cache (see
    ///     <c>HtmlSerializer</c>), and an ordinary browser session gets nothing for it — the bars render
    ///     themselves as HTML there.
    /// </remarks>
    protected override bool CollectsNativeChromeCore => ShellCore == RenderShell.Native;

    /// <inheritdoc />
    protected override void ReportNativeComponentCore(Component component) => _chrome.Collect(component);

    /// <inheritdoc />
    protected override void OnBeforeRenderWalk() => _chrome.Reset();

    /// <inheritdoc />
    protected override void OnAfterRenderWalk() => PushChrome();

    /// <summary>
    ///     Send this frame's chrome to the native shell.
    /// </summary>
    /// <remarks>
    ///     Delivered as an ordinary queued JS invoke rather than a new payload field. Not a shortcut:
    ///     <c>jsInvokes</c> already rides every frame, the client already drains it, and the bridge the head
    ///     injected already knows how to forward a call to native — so the descriptor reaches the platform
    ///     over three things that already exist and are already tested, instead of a fourth that would need
    ///     its own codec, its own size accounting and its own client branch.
    /// </remarks>
    private void PushChrome()
    {
        if (ShellCore != RenderShell.Native)
        {
            return;
        }

        try
        {
            var path = Services.GetService<RouteState>()?.Path ?? "/";
            var js = Services.GetRequiredService<IJSRuntime>();

            _chrome.Push(path, json => _ = js.InvokeVoidAsync("__raskNative.applyChrome", json));
        }
        catch (Exception ex)
        {
            // A bar that cannot be described must not take the page down with it — the page still renders,
            // it just keeps whatever chrome it had.
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Server",
                "[Rask.Server] failed to describe the native chrome for this frame", ex);
        }
    }

    /// <summary>Run the callback behind a native bar item the user tapped.</summary>
    internal bool TryRunChromeTap(string? id) => _chrome.TryRunTap(id);
}
