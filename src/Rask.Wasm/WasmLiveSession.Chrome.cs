using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Chrome;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Core.Routing;

namespace Rask.Wasm;

internal sealed partial class WasmLiveSession
{
    private readonly HostedChromePusher _chrome = new();

    // Read once, at construction: whether this page is displayed inside a native shell. It cannot change
    // afterwards — a page does not move between a browser tab and a platform WebView — and re-asking JS on
    // every render walk would put an interop call on the render's hot path.
    private readonly RenderShell _shell =
        string.Equals(JSInterop.GetShell(), "native", StringComparison.OrdinalIgnoreCase)
            ? RenderShell.Native
            : RenderShell.Web;

    /// <inheritdoc />
    protected override RenderShell ShellCore => _shell;

    /// <inheritdoc />
    /// <remarks>
    ///     Only inside a native shell: collecting costs the clean-subtree render cache, and an ordinary
    ///     browser tab gets nothing for it — the bars render themselves as HTML there.
    /// </remarks>
    protected override bool CollectsNativeChromeCore => _shell == RenderShell.Native;

    /// <inheritdoc />
    protected override void ReportNativeComponentCore(Component component) => _chrome.Collect(component);

    /// <inheritdoc />
    protected override void OnBeforeRenderWalk() => _chrome.Reset();

    /// <inheritdoc />
    protected override void OnAfterRenderWalk() => PushChrome();

    /// <summary>Send this frame's chrome to the native shell.</summary>
    /// <remarks>
    ///     Over the same queued-JS-invoke channel the Server host uses, and for the same reason: the shell's
    ///     bridge already exposes <c>__raskNative.applyChrome</c>, and the frame already carries invokes the
    ///     client drains. One delivery path, both hosting models.
    /// </remarks>
    private void PushChrome()
    {
        if (_shell != RenderShell.Native)
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
            // Bars that cannot be described must not take the page down — it still renders, it just keeps
            // whatever chrome it already had.
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Wasm",
                "[Rask.Wasm] failed to describe the native chrome for this frame", ex);
        }
    }

    /// <summary>
    ///     Run the callback behind a bar item the shell reported, then re-render.
    /// </summary>
    /// <remarks>
    ///     Takes the dispatch lock and enters a <c>Navigator</c> handler scope exactly like a DOM event: a
    ///     bar button that calls <c>Navigator.NavigateTo</c> must work, and its history push must reach the
    ///     client. Renders even when the body is unchanged, because a tap can alter only the bars — a badge,
    ///     a selected tab — and that update still has to go out.
    /// </remarks>
    internal async Task DispatchChromeTapAsync(string id)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var navigator = Services.GetRequiredService<Navigator>();
            using (navigator.EnterHandler())
            {
                if (!_chrome.TryRunTap(id))
                {
                    return;
                }

                string? historyUrl = null;
                var historyReplace = false;
                if (navigator.TryConsumeHistory(out var url, out var replace))
                {
                    historyUrl = url;
                    historyReplace = replace;
                }

                await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace).ConfigureAwait(false);
                if (await TryEmitFrameAsync(historyUrl is not null).ConfigureAwait(false))
                {
                    _htmlBuffers.Commit();
                }
            }
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Wasm", $"Rask WASM native bar tap '{id}' threw", ex);
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }
}
